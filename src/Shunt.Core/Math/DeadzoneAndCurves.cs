using System;
using System.Collections.Generic;
using System.Linq;
using Shunt.Core.Models;

namespace Shunt.Core.MathTransform;

/// <summary>
/// A control point along a response/sensitivity curve.
/// </summary>
public readonly record struct CurvePoint(float Input, float Output);

/// <summary>
/// Configurable deadzone settings for analog sticks or triggers.
/// </summary>
public record DeadzoneConfig
{
    public float InnerDeadzone { get; init; } = 0.08f; // 8% inner deadzone
    public float OuterDeadzone { get; init; } = 0.02f; // 2% outer threshold
    public bool Circular { get; init; } = true;
}

/// <summary>
/// Mathematical transformation pipeline for analog stick deadzones and sensitivity curves.
/// </summary>
public static class StickTransform
{
    /// <summary>
    /// Applies circular or axial deadzone scaling to a stick position.
    /// Rescales remaining active range linearly from 0.0 to 1.0.
    /// </summary>
    public static StickPosition ApplyDeadzone(StickPosition input, DeadzoneConfig config)
    {
        if (config.Circular)
        {
            float magnitude = MathF.Sqrt(input.X * input.X + input.Y * input.Y);
            if (magnitude <= config.InnerDeadzone)
                return StickPosition.Center;

            float maxMag = 1.0f - config.OuterDeadzone;
            if (magnitude >= maxMag)
            {
                // Normalize to outer limit
                float normX = input.X / magnitude;
                float normY = input.Y / magnitude;
                return new StickPosition(normX, normY);
            }

            // Remap magnitude linearly between inner and outer
            float scaledMag = (magnitude - config.InnerDeadzone) / (maxMag - config.InnerDeadzone);
            float factor = scaledMag / magnitude;
            return new StickPosition(input.X * factor, input.Y * factor);
        }
        else
        {
            // Axial deadzone (applied independently to X and Y)
            float x = ApplyAxisDeadzone(input.X, config.InnerDeadzone, config.OuterDeadzone);
            float y = ApplyAxisDeadzone(input.Y, config.InnerDeadzone, config.OuterDeadzone);
            return new StickPosition(x, y);
        }
    }

    /// <summary>
    /// Converts circular stick inputs into square space so diagonal deflections can reach (1.0, 1.0)
    /// without angular distortion.
    /// </summary>
    public static StickPosition ToSquare(StickPosition input)
    {
        if (MathF.Abs(input.X) < 1e-5f && MathF.Abs(input.Y) < 1e-5f)
            return StickPosition.Center;

        float r = MathF.Sqrt(input.X * input.X + input.Y * input.Y);
        float maxCoord = MathF.Max(MathF.Abs(input.X), MathF.Abs(input.Y));
        if (maxCoord < 1e-5f)
            return StickPosition.Center;

        float factor = r / maxCoord;
        float x = Math.Clamp(input.X * factor, -1.0f, 1.0f);
        float y = Math.Clamp(input.Y * factor, -1.0f, 1.0f);
        return new StickPosition(x, y);
    }

    public static float ApplyAxisDeadzone(float val, float inner, float outer)
    {
        float abs = MathF.Abs(val);
        if (abs <= inner) return 0f;
        float maxVal = 1.0f - outer;
        if (abs >= maxVal) return MathF.Sign(val);

        float scaled = (abs - inner) / (maxVal - inner);
        return MathF.Sign(val) * scaled;
    }

    /// <summary>
    /// Evaluates a piecewise linear response curve given a sorted sequence of points.
    /// </summary>
    public static float EvaluateCurve(float input, IReadOnlyList<CurvePoint> points)
    {
        if (points == null || points.Count == 0)
            return input;

        float absIn = MathF.Abs(input);
        float sign = MathF.Sign(input);

        if (absIn <= points[0].Input)
            return sign * points[0].Output;

        if (absIn >= points[^1].Input)
            return sign * points[^1].Output;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];

            if (absIn >= p1.Input && absIn <= p2.Input)
            {
                float t = (absIn - p1.Input) / (p2.Input - p1.Input);
                float outVal = p1.Output + t * (p2.Output - p1.Output);
                return sign * outVal;
            }
        }

        return input;
    }
}

/// <summary>
/// 4-point parametric cubic Bezier response curve on [0, 1] x [0, 1] Cartesian plane.
/// Uses Newton-Raphson iteration to find t for given x, then calculates output y = By(t).
/// </summary>
public record CubicBezierCurve
{
    public float X1 { get; init; } = 0.33f;
    public float Y1 { get; init; } = 0.33f;
    public float X2 { get; init; } = 0.66f;
    public float Y2 { get; init; } = 0.66f;

    public static readonly CubicBezierCurve Linear = new(0.33f, 0.33f, 0.66f, 0.66f);
    public static readonly CubicBezierCurve Instant = new(0.05f, 0.60f, 0.20f, 0.95f);
    public static readonly CubicBezierCurve Smooth = new(0.40f, 0.15f, 0.70f, 0.45f);
    public static readonly CubicBezierCurve Aggressive = new(0.60f, 0.10f, 0.40f, 0.90f);

    public CubicBezierCurve() { }

    public CubicBezierCurve(float x1, float y1, float x2, float y2)
    {
        X1 = Math.Clamp(x1, 0f, 1f);
        Y1 = Math.Clamp(y1, 0f, 1f);
        X2 = Math.Clamp(x2, 0f, 1f);
        Y2 = Math.Clamp(y2, 0f, 1f);
    }

    /// <summary>
    /// Evaluates output value for input in range [-1.0, 1.0].
    /// Negative inputs preserve sign symmetrically.
    /// </summary>
    public float Evaluate(float input)
    {
        float sign = MathF.Sign(input);
        float x = Math.Clamp(MathF.Abs(input), 0f, 1f);

        if (x <= 0f) return 0f;
        if (x >= 1f) return sign * 1f;

        // Newton-Raphson to solve Bx(t) = x
        float t = x; // Initial estimate
        for (int i = 0; i < 8; i++)
        {
            float f = SampleX(t) - x;
            if (MathF.Abs(f) < 1e-5f) break;
            float df = SampleXDerivative(t);
            if (MathF.Abs(df) < 1e-6f) break;
            t = Math.Clamp(t - f / df, 0f, 1f);
        }

        float y = SampleY(t);
        return sign * Math.Clamp(y, 0f, 1f);
    }

    private float SampleX(float t)
    {
        float inv = 1f - t;
        return 3f * inv * inv * t * X1 + 3f * inv * t * t * X2 + t * t * t;
    }

    private float SampleXDerivative(float t)
    {
        float inv = 1f - t;
        return 3f * inv * inv * X1 + 6f * inv * t * (X2 - X1) + 3f * t * t * (1f - X2);
    }

    private float SampleY(float t)
    {
        float inv = 1f - t;
        return 3f * inv * inv * t * Y1 + 3f * inv * t * t * Y2 + t * t * t;
    }
}

/// <summary>Deflection zone classification for analog sticks and triggers.</summary>
public enum DeflectionZone { None, Low, Medium, High }

public record ZoneThresholds(float LowThreshold = 0.35f, float HighThreshold = 0.75f)
{
    public DeflectionZone Classify(float magnitude)
    {
        if (magnitude < 0.05f) return DeflectionZone.None;
        if (magnitude < LowThreshold) return DeflectionZone.Low;
        if (magnitude < HighThreshold) return DeflectionZone.Medium;
        return DeflectionZone.High;
    }
}

/// <summary>DualSense adaptive trigger motor effect profile.</summary>
public enum AdaptiveTriggerType { Rigid, Pulse, Bow, MachineGun, SemiAutomatic }

public record AdaptiveTriggerEffect(
    AdaptiveTriggerType Type,
    float StartPosition = 0f,
    float EndPosition = 1f,
    float Force = 1f,
    float FrequencyHz = 10f);

