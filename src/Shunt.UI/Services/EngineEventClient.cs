using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Shunt.UI.Services;

public sealed record KeyPressedEvent(
    string DeviceId,
    string DeviceType,
    string ButtonId,
    string ButtonName,
    DateTimeOffset Timestamp);

public sealed record VibrationFeedbackEvent(
    byte LargeMotor,
    byte SmallMotor,
    int LedNumber,
    long Timestamp);

/// <summary>
/// Server-Sent Events (SSE) client consuming the streaming telemetry from the engine
/// at <c>/v2.6/Events</c> (plan 09 §3, UI Phase 4).
/// Provides auto-reconnect with exponential backoff, background stream parsing, and
/// marshals events onto the WPF Dispatcher thread for safe UI updates.
/// </summary>
public sealed class EngineEventClient : IDisposable
{
    private readonly Uri _baseAddress;
    private readonly Dispatcher? _dispatcher;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    private CancellationTokenSource? _cts;
    private Task? _streamTask;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public bool IsRunning { get; private set; }

    public event Action<KeyPressedEvent>? KeyPressedReceived;
    public event Action<VibrationFeedbackEvent>? VibrationFeedbackReceived;
    public event Action<string, string>? EventReceived;
    public event Action<bool>? ConnectionStateChanged;

    public EngineEventClient(Uri baseAddress, Dispatcher? dispatcher = null)
    {
        _baseAddress = baseAddress;
        _dispatcher = dispatcher;
        _client = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsClient = true;
    }

    public EngineEventClient(HttpClient client, Dispatcher? dispatcher = null)
    {
        _baseAddress = client.BaseAddress ?? new Uri("http://127.0.0.1:35474/");
        _dispatcher = dispatcher;
        _client = client;
        _ownsClient = false;
    }

    public void Start()
    {
        if (IsRunning || _disposed) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _streamTask = Task.Run(() => StreamLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try
        {
            _streamTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { /* ignore cancellation / timeout */ }
        _cts?.Dispose();
        _cts = null;
        _streamTask = null;
        SetConnected(false);
    }

    private async Task StreamLoopAsync(CancellationToken token)
    {
        var retryDelayMs = 1000;
        const int maxRetryDelayMs = 5000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "v2.6/Events");
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                SetConnected(true);
                retryDelayMs = 1000; // Reset delay on successful connection

                using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) break; // End of stream, server closed connection

                    ProcessLine(line);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetConnected(false);
            }

            SetConnected(false);

            if (token.IsCancellationRequested) break;

            try
            {
                await Task.Delay(retryDelayMs, token).ConfigureAwait(false);
                retryDelayMs = Math.Min(retryDelayMs * 2, maxRetryDelayMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetConnected(false);
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

        var json = line["data:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? eventType = null;
            if (root.TryGetProperty("Event", out var evtProp))
                eventType = evtProp.GetString();
            else if (root.TryGetProperty("Type", out var typeProp))
                eventType = typeProp.GetString();

            if (eventType == null) return;

            Dispatch(() => EventReceived?.Invoke(eventType, json));

            if (string.Equals(eventType, "GamepadKeyPressed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(eventType, "KeyPressed", StringComparison.OrdinalIgnoreCase))
            {
                var deviceId = root.TryGetProperty("DeviceId", out var dId) ? dId.GetString() ?? "" : "";
                var deviceType = root.TryGetProperty("DeviceType", out var dType) ? dType.GetString() ?? "" : "";
                var buttonId = root.TryGetProperty("ButtonId", out var bId) ? bId.ToString() : "";
                var buttonName = root.TryGetProperty("ButtonName", out var bName) ? bName.GetString() ?? "" : "";
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;
                if (root.TryGetProperty("Timestamp", out var tsProp) && tsProp.TryGetDateTimeOffset(out var parsedTs))
                {
                    timestamp = parsedTs;
                }

                var keyEvent = new KeyPressedEvent(deviceId, deviceType, buttonId, buttonName, timestamp);
                Dispatch(() => KeyPressedReceived?.Invoke(keyEvent));
            }
            else if (string.Equals(eventType, "VibrationFeedback", StringComparison.OrdinalIgnoreCase))
            {
                byte large = root.TryGetProperty("LargeMotor", out var lm) && lm.TryGetByte(out var b1) ? b1 : (byte)0;
                byte small = root.TryGetProperty("SmallMotor", out var sm) && sm.TryGetByte(out var b2) ? b2 : (byte)0;
                int led = root.TryGetProperty("LedNumber", out var ledProp) && ledProp.TryGetInt32(out var lVal) ? lVal : 0;
                long ts = root.TryGetProperty("Timestamp", out var tsProp) && tsProp.TryGetInt64(out var tsVal) ? tsVal : 0;

                var vibEvent = new VibrationFeedbackEvent(large, small, led, ts);
                Dispatch(() => VibrationFeedbackReceived?.Invoke(vibEvent));
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON line
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        Dispatch(() => ConnectionStateChanged?.Invoke(connected));
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher != null && !_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
