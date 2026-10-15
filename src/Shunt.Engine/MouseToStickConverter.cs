using System;
using Shunt.Core.Models;

namespace Shunt.Engine;

public enum BallisticPreset
{
    Linear,
    CallOfDuty,
    ApexLegends,
    Destiny2,
    HaloInfinite,
    Aggressive
}

/// <summary>
/// Converts relative mouse movements (delta X, delta Y) into smooth analog stick deflections
/// with ballistic acceleration curves, exponential moving average (EMA) smoothing, and diagonal dampening.
/// </summary>
public class MouseToStickConverter
{
    private float _currentX = 0f;
    private float _currentY = 0f;
    private float _smoothedX = 0f;
    private float _smoothedY = 0f;

    public float SensitivityX { get; set; } = 0.05f;
    public float SensitivityY { get; set; } = 0.05f;
    public float DecayRate { get; set; } = 0.85f; // Exponential decay per frame when mouse stops
    public float SmoothingAlpha { get; set; } = 0.65f; // EMA weight for incoming raw delta
    public float BallisticGamma { get; set; } = 1.35f; // Power-law acceleration curve
    public int NoiseDeadzoneThreshold { get; set; } = 1; // Discard sub-pixel jitter
    public float DiagonalDampening { get; set; } = 0.85f; // Dampen diagonal overshoot

    public void ApplyPreset(BallisticPreset preset)
    {
        switch (preset)
        {
            case BallisticPreset.Linear:
                SensitivityX = 0.05f;
                SensitivityY = 0.05f;
                BallisticGamma = 1.0f;
                SmoothingAlpha = 0.50f;
                DiagonalDampening = 1.0f;
                break;
            case BallisticPreset.CallOfDuty:
                SensitivityX = 0.06f;
                SensitivityY = 0.055f;
                BallisticGamma = 1.45f;
                SmoothingAlpha = 0.70f;
                DiagonalDampening = 0.88f;
                break;
            case BallisticPreset.ApexLegends:
                SensitivityX = 0.055f;
                SensitivityY = 0.05f;
                BallisticGamma = 1.25f;
                SmoothingAlpha = 0.60f;
                DiagonalDampening = 0.82f;
                break;
            case BallisticPreset.Destiny2:
                SensitivityX = 0.045f;
                SensitivityY = 0.045f;
                BallisticGamma = 1.30f;
                SmoothingAlpha = 0.75f;
                DiagonalDampening = 0.90f;
                break;
            case BallisticPreset.HaloInfinite:
                SensitivityX = 0.05f;
                SensitivityY = 0.05f;
                BallisticGamma = 1.10f;
                SmoothingAlpha = 0.55f;
                DiagonalDampening = 0.85f;
                break;
            case BallisticPreset.Aggressive:
                SensitivityX = 0.08f;
                SensitivityY = 0.08f;
                BallisticGamma = 1.75f;
                SmoothingAlpha = 0.80f;
                DiagonalDampening = 0.75f;
                break;
        }
    }

    public StickPosition Update(int deltaX, int deltaY)
    {
        // 1. Noise cancellation deadzone
        if (Math.Abs(deltaX) <= NoiseDeadzoneThreshold) deltaX = 0;
        if (Math.Abs(deltaY) <= NoiseDeadzoneThreshold) deltaY = 0;

        if (deltaX != 0 || deltaY != 0)
        {
            // 2. Ballistic acceleration
            float accelX = ApplyBallistics(deltaX * SensitivityX);
            float accelY = ApplyBallistics(deltaY * SensitivityY);

            // 3. Diagonal dampening if both axes active
            if (deltaX != 0 && deltaY != 0)
            {
                accelX *= DiagonalDampening;
                accelY *= DiagonalDampening;
            }

            // 4. Exponential moving average (EMA) filter
            _smoothedX = SmoothingAlpha * accelX + (1f - SmoothingAlpha) * _smoothedX;
            _smoothedY = SmoothingAlpha * accelY + (1f - SmoothingAlpha) * _smoothedY;

            _currentX = Math.Clamp(_currentX + _smoothedX, -1.0f, 1.0f);
            _currentY = Math.Clamp(_currentY - _smoothedY, -1.0f, 1.0f); // Invert Y for standard stick direction
        }
        else
        {
            // Decay towards zero when no mouse movement
            _currentX *= DecayRate;
            _currentY *= DecayRate;
            _smoothedX *= DecayRate;
            _smoothedY *= DecayRate;

            if (MathF.Abs(_currentX) < 0.005f) _currentX = 0f;
            if (MathF.Abs(_currentY) < 0.005f) _currentY = 0f;
        }

        return new StickPosition(_currentX, _currentY);
    }

    private float ApplyBallistics(float normalizedVal)
    {
        float sign = MathF.Sign(normalizedVal);
        float mag = MathF.Abs(normalizedVal);
        return sign * MathF.Pow(mag, BallisticGamma);
    }

    public void Reset()
    {
        _currentX = 0f;
        _currentY = 0f;
        _smoothedX = 0f;
        _smoothedY = 0f;
    }
}
