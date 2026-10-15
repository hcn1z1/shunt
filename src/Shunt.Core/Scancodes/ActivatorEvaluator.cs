using System;
using System.Diagnostics;

namespace Shunt.Core.Scancodes;

/// <summary>
/// The 6 activator conditions of the reWASD state machine (plan 08 §3).
/// </summary>
public enum ActivatorType
{
    Single,
    Double,
    Triple,
    Long,
    Start,
    Release
}

/// <summary>
/// Mutable timing state for one activator binding.
/// </summary>
public class ActivatorEvaluationState
{
    public bool WasPressed { get; set; }
    public long PressStartedTimestamp { get; set; }
    public long LastTapTimestamp { get; set; }
    public int TapCount { get; set; }
    public bool WasOutputting { get; set; }
}

/// <summary>
/// Polymorphic activator evaluation engine: evaluates the 6 activator conditions
/// (Single, Double, Triple, Long, Start, Release) with configurable threshold windows
/// (plan 08 §3). Pure and allocation-free per call.
///
/// State machine:
///   Idle --KeyDown--> Pressed
///   Pressed --KeyUp within window--> Released
///   Released --KeyDown within 150ms--> DoublePress --3rd KeyDown--> TriplePress
///   Pressed --hold > threshold--> LongPress --KeyUp--> ReleasePress
/// </summary>
public static class ActivatorEvaluator
{
    public const int DefaultTapWindowMs = 150;
    public const int DefaultLongPressThresholdMs = 350;

    /// <summary>
    /// Evaluates whether the activator should fire for the current physical state.
    /// </summary>
    /// <param name="type">Activator condition to evaluate.</param>
    /// <param name="state">Per-binding mutable state.</param>
    /// <param name="pressed">Current physical pressed state.</param>
    /// <param name="now">Current Stopwatch timestamp.</param>
    /// <param name="holdThresholdMs">Hold window for the Long activator.</param>
    /// <param name="tapWindowMs">Inter-tap window for Double/Triple activators.</param>
    /// <returns>True when the activator output is active this frame.</returns>
    public static bool Evaluate(
        ActivatorType type,
        ActivatorEvaluationState state,
        bool pressed,
        long now,
        int holdThresholdMs = DefaultLongPressThresholdMs,
        int tapWindowMs = DefaultTapWindowMs)
    {
        bool rising = pressed && !state.WasPressed;
        bool falling = !pressed && state.WasPressed;
        if (rising) state.PressStartedTimestamp = now;
        double elapsedMs = ElapsedMs(state.PressStartedTimestamp, now);

        bool active = type switch
        {
            ActivatorType.Single => pressed,
            ActivatorType.Start => rising,
            ActivatorType.Release => falling,
            ActivatorType.Long => pressed && elapsedMs >= Math.Max(1, holdThresholdMs),
            ActivatorType.Double => IsTapCount(state, falling, now, 2, tapWindowMs),
            ActivatorType.Triple => IsTapCount(state, falling, now, 3, tapWindowMs),
            _ => false
        };

        state.WasPressed = pressed;
        return active;
    }

    /// <summary>Elapsed milliseconds between two Stopwatch timestamps.</summary>
    public static double ElapsedMs(long startTimestamp, long now) =>
        (now - startTimestamp) * 1000d / Stopwatch.Frequency;

    private static bool IsTapCount(ActivatorEvaluationState state, bool falling, long now, int requiredTaps, int windowMs)
    {
        if (!falling) return false;

        double sinceLastTap = state.LastTapTimestamp != 0 ? ElapsedMs(state.LastTapTimestamp, now) : double.MaxValue;
        state.TapCount = sinceLastTap <= Math.Max(1, windowMs) ? state.TapCount + 1 : 1;
        state.LastTapTimestamp = now;
        if (state.TapCount < requiredTaps) return false;
        state.TapCount = 0;
        state.LastTapTimestamp = 0;
        return true;
    }
}
