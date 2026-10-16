using System;

namespace Shunt.Engine;

public enum MouseZone { Deadzone, Low, Medium, High }
public enum MouseDirection { Center, Right, DownRight, Down, DownLeft, Left, UpLeft, Up, UpRight }

public sealed record MouseZoneConfig(
    float DeadzoneRadius = 0.05f,
    float LowRadius = 0.30f,
    float MediumRadius = 0.70f,
    float HighRadius = 0.92f,
    float LowMultiplier = 0.5f,
    float MediumMultiplier = 1.0f,
    float HighMultiplier = 2.2f,
    string? EdgeAction = null)
{
    public void Validate()
    {
        if (DeadzoneRadius < 0 || LowRadius <= DeadzoneRadius || MediumRadius <= LowRadius ||
            HighRadius <= MediumRadius || HighRadius > 1f)
            throw new ArgumentOutOfRangeException(nameof(MouseZoneConfig), "Mouse zone radii must increase from deadzone through high zone and stay within 0..1.");
        if (LowMultiplier < 0 || MediumMultiplier < 0 || HighMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(MouseZoneConfig), "Mouse zone multipliers cannot be negative.");
    }
}

public readonly record struct MouseZoneResult(
    MouseZone Zone,
    MouseDirection Direction,
    float Radius,
    float AngleDegrees,
    float SensitivityMultiplier,
    string? EdgeAction)
{
    public bool IsEdge => Zone == MouseZone.High && EdgeAction is not null;
}

/// <summary>Classifies normalized mouse or stick deflection into radial zones and sweep sectors.</summary>
public static class MouseZoneEvaluator
{
    public static MouseZoneResult Evaluate(float x, float y, MouseZoneConfig? config = null, int sectors = 8)
    {
        config ??= new MouseZoneConfig();
        config.Validate();
        if (sectors is not (4 or 8)) throw new ArgumentOutOfRangeException(nameof(sectors), "Direction sectors must be four or eight.");
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x), "Deflection coordinates must be finite.");

        var rawRadius = MathF.Sqrt(x * x + y * y);
        var radius = Math.Clamp(rawRadius, 0f, 1f);
        var angle = rawRadius < 0.0001f ? 0f : MathF.Atan2(y, x) * (180f / MathF.PI);
        var zone = radius < config.DeadzoneRadius ? MouseZone.Deadzone
            : radius < config.LowRadius ? MouseZone.Low
            : radius < config.MediumRadius ? MouseZone.Medium
            : MouseZone.High;
        var multiplier = zone switch
        {
            MouseZone.Low => config.LowMultiplier,
            MouseZone.Medium => config.MediumMultiplier,
            MouseZone.High => config.HighMultiplier,
            _ => 0f
        };
        var direction = zone == MouseZone.Deadzone ? MouseDirection.Center : GetDirection(angle, rawRadius, sectors);
        var action = zone == MouseZone.High && radius >= config.HighRadius ? config.EdgeAction : null;
        return new MouseZoneResult(zone, direction, radius, angle, multiplier, action);
    }

    private static MouseDirection GetDirection(float angle, float radius, int sectors)
    {
        if (radius < 0.0001f) return MouseDirection.Center;
        // Screen coordinates: positive Y points down. Four-way sectors use cardinal directions;
        // eight-way sectors split each cardinal boundary at 22.5 degrees.
        var normalized = (angle + 360f) % 360f;
        if (sectors == 4)
        {
            if (normalized >= 45f && normalized < 135f) return MouseDirection.Down;
            if (normalized >= 135f && normalized < 225f) return MouseDirection.Left;
            if (normalized >= 225f && normalized < 315f) return MouseDirection.Up;
            return MouseDirection.Right;
        }

        return ((int)MathF.Floor((normalized + 22.5f) / 45f) % 8) switch
        {
            0 => MouseDirection.Right,
            1 => MouseDirection.DownRight,
            2 => MouseDirection.Down,
            3 => MouseDirection.DownLeft,
            4 => MouseDirection.Left,
            5 => MouseDirection.UpLeft,
            6 => MouseDirection.Up,
            _ => MouseDirection.UpRight
        };
    }
}
