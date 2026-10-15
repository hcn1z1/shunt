using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Shunt.Core.Interfaces;
using Shunt.Core.Models;

namespace Shunt.Engine;

public enum MacroOpCode
{
    None = 0,
    KeyDown = 1,
    KeyUp = 2,
    Delay = 3,
    RumblePulse = 4,
    LoopGoto = 5
}

public record MacroStepInstruction(
    MacroOpCode Op,
    GamepadButtons Button = GamepadButtons.None,
    int DelayMs = 0,
    int JumpIndex = 0,
    int RepeatCount = 1);

/// <summary>
/// Handles turbo button pulses, toggle states, and high-precision macro sequence execution.
/// </summary>
public class MacroEngine : IDisposable
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private readonly ConcurrentDictionary<GamepadButtons, bool> _toggleStates = new();
    private readonly ConcurrentDictionary<GamepadButtons, long> _turboLastToggleTick = new();
    private readonly ConcurrentDictionary<GamepadButtons, bool> _turboActiveState = new();
    private readonly ConcurrentDictionary<GamepadButtons, CancellationTokenSource> _runningMacros = new();
    private readonly bool _multimediaTimerSet;

    public int TurboRateHz { get; set; } = 20; // 20 pulses per second

    public MacroEngine()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TimeBeginPeriod(1);
                _multimediaTimerSet = true;
            }
        }
        catch
        {
            // Fallback gracefully on platforms without winmm.dll
        }
    }

    /// <summary>
    /// Evaluates turbo for a button that is physically held down.
    /// Toggles state at TurboRateHz interval.
    /// </summary>
    public bool EvaluateTurbo(GamepadButtons button, bool physicallyHeld, long currentMilliseconds)
    {
        if (!physicallyHeld)
        {
            _turboActiveState.TryRemove(button, out _);
            _turboLastToggleTick.TryRemove(button, out _);
            return false;
        }

        long intervalMs = 1000 / Math.Max(1, TurboRateHz);
        long lastTick = _turboLastToggleTick.GetOrAdd(button, currentMilliseconds);

        if (currentMilliseconds - lastTick >= intervalMs)
        {
            bool current = _turboActiveState.GetOrAdd(button, false);
            bool next = !current;
            _turboActiveState[button] = next;
            _turboLastToggleTick[button] = currentMilliseconds;
            return next;
        }

        return _turboActiveState.GetValueOrDefault(button, true);
    }

    /// <summary>
    /// Evaluates toggle mode for a button. Toggles on every press transition (rising edge).
    /// </summary>
    public bool EvaluateToggle(GamepadButtons button, bool isPressedThisFrame, bool wasPressedLastFrame)
    {
        if (isPressedThisFrame && !wasPressedLastFrame)
        {
            bool current = _toggleStates.GetValueOrDefault(button, false);
            bool next = !current;
            _toggleStates[button] = next;
            return next;
        }

        return _toggleStates.GetValueOrDefault(button, false);
    }

    /// <summary>
    /// Triggers a macro sequence execution for a button trigger.
    /// </summary>
    public void StartMacro(GamepadButtons trigger, IReadOnlyList<MacroStepInstruction> steps, IVirtualGamepad? targetPad)
    {
        if (steps == null || steps.Count == 0 || targetPad == null) return;

        StopMacro(trigger);

        var cts = new CancellationTokenSource();
        _runningMacros[trigger] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteSequenceAsync(steps, targetPad, cts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _runningMacros.TryRemove(trigger, out _);
            }
        }, cts.Token);
    }

    public void StopMacro(GamepadButtons trigger)
    {
        if (_runningMacros.TryRemove(trigger, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// High-precision execution of compiled macro steps (< 1ms precision using Stopwatch spinning).
    /// </summary>
    public static async Task ExecuteSequenceAsync(
        IReadOnlyList<MacroStepInstruction> steps,
        IVirtualGamepad targetPad,
        CancellationToken ct)
    {
        int loopCount = 0;
        int pc = 0;
        var currentButtons = GamepadButtons.None;

        while (pc < steps.Count && !ct.IsCancellationRequested)
        {
            var step = steps[pc];
            switch (step.Op)
            {
                case MacroOpCode.KeyDown:
                    currentButtons |= step.Button;
                    targetPad.UpdateState(new GamepadState { Buttons = currentButtons });
                    pc++;
                    break;

                case MacroOpCode.KeyUp:
                    currentButtons &= ~step.Button;
                    targetPad.UpdateState(new GamepadState { Buttons = currentButtons });
                    pc++;
                    break;

                case MacroOpCode.Delay:
                    if (step.DelayMs > 0)
                    {
                        await PreciseDelayAsync(step.DelayMs, ct);
                    }
                    pc++;
                    break;

                case MacroOpCode.RumblePulse:
                    targetPad.SetVibration(255, 255);
                    await PreciseDelayAsync(step.DelayMs > 0 ? step.DelayMs : 100, ct);
                    targetPad.SetVibration(0, 0);
                    pc++;
                    break;

                case MacroOpCode.LoopGoto:
                    loopCount++;
                    if (step.RepeatCount <= 0 || loopCount < step.RepeatCount)
                    {
                        pc = Math.Clamp(step.JumpIndex, 0, steps.Count - 1);
                    }
                    else
                    {
                        loopCount = 0;
                        pc++;
                    }
                    break;

                default:
                    pc++;
                    break;
            }
        }
    }

    /// <summary>
    /// Delays for the specified milliseconds with high resolution by combining Task.Delay with Stopwatch spin.
    /// </summary>
    public static async Task PreciseDelayAsync(int milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0) return;

        if (milliseconds > 2)
        {
            // Coarse sleep for milliseconds - 2
            await Task.Delay(milliseconds - 2, ct);
        }

        // Spin-wait for remaining high precision fraction
        long start = Stopwatch.GetTimestamp();
        long targetTicks = (long)(milliseconds * Stopwatch.Frequency / 1000.0);
        while ((Stopwatch.GetTimestamp() - start) < targetTicks)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(10);
        }
    }

    public void Reset()
    {
        foreach (var kvp in _runningMacros)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _runningMacros.Clear();
        _toggleStates.Clear();
        _turboLastToggleTick.Clear();
        _turboActiveState.Clear();
    }

    public void Dispose()
    {
        Reset();
        if (_multimediaTimerSet)
        {
            try { TimeEndPeriod(1); } catch { }
        }
    }
}
