using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;

namespace Shunt.Engine;

/// <summary>
/// A single physical button-press event surfaced by the sniffer loop (plan 09 §3.1).
/// </summary>
public record KeyPressedEventArgs(
    string DeviceId,
    string DeviceType,
    string ButtonId,
    string ButtonName,
    DateTimeOffset Timestamp);

/// <summary>
/// Minimal SSE publisher abstraction so the sniffer stays transport-agnostic
/// (satisfied by <see cref="EngineEventBroker"/> in the HTTP host, plan 09 §3.2).
/// </summary>
public interface ISSEEventPublisher
{
    Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hardware button auto-focus sniffer: whenever the user presses any physical key or button
/// on any discovered controller, the poller detects the rising edge and pushes a
/// <c>KeyPressed</c> event to the SSE stream so the UI immediately lights up and selects
/// that button on the virtual canvas (plan 09 §3).
/// </summary>
public sealed class KeyPressedPollerService : IDisposable
{
    private readonly IHardwareDiscoveryService _hardwareManager;
    private readonly ISSEEventPublisher _ssePublisher;
    private readonly Dictionary<string, GamepadButtons> _lastStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    /// <summary>Polling cadence — 100Hz UI sniffer loop (plan 09 §3.2).</summary>
    public int PollIntervalMs { get; set; } = 10;

    /// <summary>Extra listener for direct consumers (tests, UI detection mode).</summary>
    public event Action<KeyPressedEventArgs>? KeyPressed;

    public KeyPressedPollerService(IHardwareDiscoveryService hardwareManager, ISSEEventPublisher ssePublisher)
    {
        _hardwareManager = hardwareManager;
        _ssePublisher = ssePublisher;
    }

    public void StartPolling()
    {
        lock (_stateLock)
        {
            if (_pollTask != null) return;
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollingLoop(_cts.Token));
        }
    }

    public void StopPolling()
    {
        lock (_stateLock)
        {
            _cts?.Cancel();
            _pollTask = null;
            _cts?.Dispose();
            _cts = null;
            _lastStates.Clear();
        }
    }

    /// <summary>Diffs one controller snapshot against the previous frame (pure, testable).</summary>
    public static IReadOnlyList<(string ButtonId, string ButtonName)> DetectPressedButtons(GamepadState? previous, GamepadState? current)
    {
        var pressedNow = current?.Buttons ?? GamepadButtons.None;
        var pressedBefore = previous?.Buttons ?? GamepadButtons.None;
        var rising = pressedNow & ~pressedBefore;
        var names = new List<(string, string)>();
        foreach (GamepadButtons value in Enum.GetValues<GamepadButtons>())
        {
            if (value != GamepadButtons.None && rising.HasFlag(value))
            {
                string name = value.ToString();
                names.Add((name, name));
            }
        }
        return names;
    }

    private async Task PollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var snapshot = _hardwareManager.DiscoveredControllers
                .Where(c => c.IsConnected)
                .Select(c => (Controller: c, State: c.CurrentState))
                .ToList();

            var events = new List<KeyPressedEventArgs>();
            lock (_stateLock)
            {
                foreach (var (controller, state) in snapshot)
                {
                    _lastStates.TryGetValue(controller.Id, out var previous);
                    foreach (var (buttonId, buttonName) in DetectPressedButtons(
                                 previous == GamepadButtons.None ? null : new GamepadState { Buttons = previous }, state))
                    {
                        events.Add(new KeyPressedEventArgs(
                            controller.Id, controller.HardwareType, buttonId, buttonName, DateTimeOffset.UtcNow));
                    }
                    _lastStates[controller.Id] = state.Buttons;
                }
            }

            foreach (var evt in events)
            {
                KeyPressed?.Invoke(evt);
                await _ssePublisher.PublishAsync("KeyPressed", new
                {
                    Event = "GamepadKeyPressed",
                    evt.DeviceId,
                    evt.DeviceType,
                    evt.ButtonId,
                    evt.ButtonName,
                    Timestamp = evt.Timestamp
                }, token);
            }

            try
            {
                await Task.Delay(PollIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
