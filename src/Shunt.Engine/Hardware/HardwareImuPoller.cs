using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Shunt.Engine.Hardware;

/// <summary>Reads reports from one HID sensor collection and emits decoded motion samples.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class HardwareImuPoller : IAsyncDisposable
{
    private readonly string _devicePath;
    private readonly ImuDeviceProfile _profile;
    private readonly Func<string, SafeFileHandle> _open;
    private CancellationTokenSource? _cancellation;
    private Task? _pollTask;

    public event Action<ImuSensorSample>? SampleReceived;
    public event EventHandler<Exception>? Faulted;
    public bool IsRunning => _pollTask is { IsCompleted: false };

    public HardwareImuPoller(string devicePath, ImuDeviceProfile profile)
        : this(devicePath, profile, HidNative.OpenRead) { }

    internal HardwareImuPoller(string devicePath, ImuDeviceProfile profile, Func<string, SafeFileHandle> open)
    {
        _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("HID sensor polling requires Windows.");
        if (IsRunning) return;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollAsync(_cancellation.Token));
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;
        _cancellation.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _pollTask = null;
        _cancellation.Dispose();
        _cancellation = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var handle = _open(_devicePath);
            await using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: true);
            var report = new byte[Math.Max(_profile.ReportFormat.MinimumLength, 64)];
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(report, cancellationToken).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The HID sensor collection was disconnected.");
                var timestamp = (long)Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).TotalMilliseconds;
                if (_profile.ReportFormat.TryParse(report.AsSpan(0, count), timestamp, out var sample))
                    SampleReceived?.Invoke(sample);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Faulted?.Invoke(this, ex);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
