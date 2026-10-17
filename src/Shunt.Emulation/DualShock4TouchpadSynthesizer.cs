using System;
using System.Collections.Generic;
using Shunt.Core.Models;

namespace Shunt.Emulation;

/// <summary>
/// A single frame of synthesized touchpad activity produced by the gesture engine.
/// </summary>
public record TouchpadFrame(TouchPoint Touch0, TouchPoint Touch1, int DurationMs);

/// <summary>
/// Touchpad gestures the synthesizer can replay on a virtual DualShock 4 (plan 07 §5).
/// </summary>
public enum TouchpadGesture
{
    TapLeft,
    TapRight,
    TapCenter,
    TapTop,
    TapBottom,
    MultiTap,
    SwipeUp,
    SwipeDown,
    SwipeLeft,
    SwipeRight,
    TwoFingerSwipeUp,
    TwoFingerSwipeDown,
    TwoFingerSwipeLeft,
    TwoFingerSwipeRight,
    PinchZoomIn,
    PinchZoomOut
}

/// <summary>
/// Algorithmic Gesture Synthesizer: converts a discrete gesture request into a timed
/// sequence of touchpad contact frames interpolated at the touchpad report rate (plan 07 §5).
/// </summary>
public sealed class DualShock4TouchpadSynthesizer
{
    // DS4 capacitive touchpad logical surface: X 0..1919, Y 0..942
    public const ushort MaxX = 1919;
    public const ushort MaxY = 942;

    public int ReportIntervalMs { get; set; } = 8;   // ~125Hz touchpad refresh
    public int TapDurationMs { get; set; } = 50;
    public int SwipeDurationMs { get; set; } = 64;   // 8 frames @ 125Hz
    public int SwipeDistance { get; set; } = 640;    // logical units travelled
    public int PinchStartSpan { get; set; } = 320;   // distance between pinch fingers at rest

    /// <summary>
    /// Synthesizes the full contact timeline for the requested gesture.
    /// </summary>
    public IReadOnlyList<TouchpadFrame> Synthesize(TouchpadGesture gesture, byte fingerId = 0)
    {
        var frames = new List<TouchpadFrame>();
        switch (gesture)
        {
            case TouchpadGesture.TapLeft:
                frames.AddRange(Tap(480, 471, fingerId));
                break;
            case TouchpadGesture.TapRight:
                frames.AddRange(Tap(1440, 471, fingerId));
                break;
            case TouchpadGesture.TapCenter:
                frames.AddRange(Tap(960, 471, fingerId));
                break;
            case TouchpadGesture.TapTop:
                frames.AddRange(Tap(960, 235, fingerId));
                break;
            case TouchpadGesture.TapBottom:
                frames.AddRange(Tap(960, 706, fingerId));
                break;
            case TouchpadGesture.MultiTap:
                frames.AddRange(Tap(MaxX / 2, MaxY / 2, 0));
                frames.AddRange(Tap(MaxX / 2, MaxY / 2, 1));
                break;
            case TouchpadGesture.SwipeUp:
                frames.AddRange(Swipe(MaxX / 2, MaxY - 100, 0, -SwipeDistance, fingerId));
                break;
            case TouchpadGesture.SwipeDown:
                frames.AddRange(Swipe(MaxX / 2, 100, 0, SwipeDistance, fingerId));
                break;
            case TouchpadGesture.SwipeLeft:
                frames.AddRange(Swipe(MaxX - 100, MaxY / 2, -SwipeDistance, 0, fingerId));
                break;
            case TouchpadGesture.SwipeRight:
                frames.AddRange(Swipe(100, MaxY / 2, SwipeDistance, 0, fingerId));
                break;
            case TouchpadGesture.TwoFingerSwipeUp:
                frames.AddRange(ParallaxSwipe(0, -SwipeDistance));
                break;
            case TouchpadGesture.TwoFingerSwipeDown:
                frames.AddRange(ParallaxSwipe(0, SwipeDistance));
                break;
            case TouchpadGesture.TwoFingerSwipeLeft:
                frames.AddRange(ParallaxSwipe(-SwipeDistance, 0));
                break;
            case TouchpadGesture.TwoFingerSwipeRight:
                frames.AddRange(ParallaxSwipe(SwipeDistance, 0));
                break;
            case TouchpadGesture.PinchZoomIn:
                frames.AddRange(Pinch(outward: true));
                break;
            case TouchpadGesture.PinchZoomOut:
                frames.AddRange(Pinch(outward: false));
                break;
        }
        return frames;
    }

    /// <summary>
    /// Tap: instant touch down at normalized region coordinates held ~50ms, then lift.
    /// </summary>
    private IEnumerable<TouchpadFrame> Tap(ushort x, ushort y, byte fingerId)
    {
        int elapsed = 0;
        while (elapsed < TapDurationMs)
        {
            int step = Math.Min(ReportIntervalMs, TapDurationMs - elapsed);
            yield return new TouchpadFrame(new TouchPoint(true, fingerId, x, y), default, step);
            elapsed += step;
        }
        yield return new TouchpadFrame(default, default, ReportIntervalMs);
    }

    /// <summary>
    /// 1-Finger Swipe: 8 interpolated frames at 125Hz moving by the requested delta.
    /// </summary>
    private IEnumerable<TouchpadFrame> Swipe(int x0, int y0, int deltaX, int deltaY, byte fingerId)
    {
        int frames = Math.Max(2, SwipeDurationMs / ReportIntervalMs);
        // Establish contact at the start point before moving; omitting this sample
        // can make the first point look like a touch that appeared mid-swipe.
        yield return new TouchpadFrame(new TouchPoint(true, fingerId, (ushort)ClampX(x0), (ushort)ClampY(y0)), default, ReportIntervalMs);
        for (int i = 1; i <= frames; i++)
        {
            int x = ClampX(x0 + deltaX * i / frames);
            int y = ClampY(y0 + deltaY * i / frames);
            yield return new TouchpadFrame(new TouchPoint(true, fingerId, (ushort)x, (ushort)y), default, ReportIntervalMs);
        }
        yield return new TouchpadFrame(default, default, ReportIntervalMs);
    }

    /// <summary>
    /// 2-Finger Swipe: two parallel contacts offset horizontally on the surface.
    /// </summary>
    private IEnumerable<TouchpadFrame> ParallaxSwipe(int deltaX, int deltaY)
    {
        int frames = Math.Max(2, SwipeDurationMs / ReportIntervalMs);
        int halfSpan = PinchStartSpan / 2;
        for (int i = 1; i <= frames; i++)
        {
            int cx = MaxX / 2 + deltaX * i / frames;
            int cy = MaxY / 2 + deltaY * i / frames;
            var t0 = new TouchPoint(true, 0, (ushort)ClampX(cx - halfSpan), (ushort)ClampY(cy));
            var t1 = new TouchPoint(true, 1, (ushort)ClampX(cx + halfSpan), (ushort)ClampY(cy));
            yield return new TouchpadFrame(t0, t1, ReportIntervalMs);
        }
        yield return new TouchpadFrame(default, default, ReportIntervalMs);
    }

    /// <summary>
    /// Pinch Zoom In: dual touch points at (960±160, 500) moving outward.
    /// Pinch Zoom Out: dual touch points moving inward toward center.
    /// </summary>
    private IEnumerable<TouchpadFrame> Pinch(bool outward)
    {
        int frames = Math.Max(2, SwipeDurationMs / ReportIntervalMs);
        int startX = MaxX / 2 - PinchStartSpan / 2;
        int endX = MaxX / 2 - (PinchStartSpan + SwipeDistance) / 2;
        if (!outward) (startX, endX) = (endX, startX);

        for (int i = 1; i <= frames; i++)
        {
            int lx = startX + (endX - startX) * i / frames;
            int rx = MaxX - lx;
            var t0 = new TouchPoint(true, 0, (ushort)ClampX(lx), 500);
            var t1 = new TouchPoint(true, 1, (ushort)ClampX(rx), 500);
            yield return new TouchpadFrame(t0, t1, ReportIntervalMs);
        }
        yield return new TouchpadFrame(default, default, ReportIntervalMs);
    }

    private static int ClampX(int value) => Math.Clamp(value, 0, MaxX);
    private static int ClampY(int value) => Math.Clamp(value, 0, MaxY);
}
