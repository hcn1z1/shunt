using Shunt.Core.Models;
using Shunt.Engine.Hardware;

namespace Shunt.Engine;

/// <summary>Resolves Shunt Mode stick chords and gates physical IMU samples before DS4 output.</summary>
public sealed class XboxToDualShock4ShuntService
{
    public const long ChordWindowMilliseconds = 40;

    private GamepadButtons _previousButtons;
    private long? _leftThumbPressedAt;
    private long? _rightThumbPressedAt;
    private bool _previousGyroChord;
    private bool _gyroToggledOn;

    public GyroActivatorMode ActivationMode { get; set; } = GyroActivatorMode.Toggle;
    public bool IsGyroEnabled { get; private set; }
    public event Action<bool>? GyroStateChanged;

    public GamepadState Process(GamepadState input, long timestampMilliseconds, ImuSensorSample? imuSample = null)
    {
        var buttons = input.Buttons;
        var leftPressed = (buttons & GamepadButtons.LeftThumb) != 0;
        var rightPressed = (buttons & GamepadButtons.RightThumb) != 0;
        var leftWasPressed = (_previousButtons & GamepadButtons.LeftThumb) != 0;
        var rightWasPressed = (_previousButtons & GamepadButtons.RightThumb) != 0;

        if (leftPressed && !leftWasPressed) _leftThumbPressedAt = timestampMilliseconds;
        if (rightPressed && !rightWasPressed) _rightThumbPressedAt = timestampMilliseconds;
        if (!leftPressed) _leftThumbPressedAt = null;
        if (!rightPressed) _rightThumbPressedAt = null;

        var chordFormed = leftPressed && rightPressed &&
                          _leftThumbPressedAt.HasValue && _rightThumbPressedAt.HasValue &&
                          Math.Abs(_leftThumbPressedAt.Value - _rightThumbPressedAt.Value) <= ChordWindowMilliseconds;
        var chordRising = chordFormed && !_previousGyroChord;
        if (ActivationMode == GyroActivatorMode.Hold)
        {
            SetGyroEnabled(chordFormed);
        }
        else if (chordRising)
        {
            _gyroToggledOn = !_gyroToggledOn;
            SetGyroEnabled(_gyroToggledOn);
        }

        var outputButtons = buttons;
        if (chordFormed)
        {
            outputButtons &= ~(GamepadButtons.LeftThumb | GamepadButtons.RightThumb);
        }
        else
        {
            if (leftPressed && _leftThumbPressedAt.HasValue && timestampMilliseconds - _leftThumbPressedAt.Value < ChordWindowMilliseconds)
                outputButtons &= ~GamepadButtons.LeftThumb;
            if (rightPressed && _rightThumbPressedAt.HasValue && timestampMilliseconds - _rightThumbPressedAt.Value < ChordWindowMilliseconds)
                outputButtons &= ~GamepadButtons.RightThumb;
        }

        _previousGyroChord = chordFormed;
        _previousButtons = buttons;

        if (imuSample is { } sample)
        {
            return input with
            {
                Buttons = outputButtons,
                GyroVelX = IsGyroEnabled ? sample.GyroX : 0f,
                GyroVelY = IsGyroEnabled ? sample.GyroY : 0f,
                GyroVelZ = IsGyroEnabled ? sample.GyroZ : 0f,
                AccelX = sample.AccelX,
                AccelY = sample.AccelY,
                AccelZ = sample.AccelZ
            };
        }

        return input with
        {
            Buttons = outputButtons,
            GyroVelX = IsGyroEnabled ? input.GyroVelX : 0f,
            GyroVelY = IsGyroEnabled ? input.GyroVelY : 0f,
            GyroVelZ = IsGyroEnabled ? input.GyroVelZ : 0f
        };
    }

    public void Reset()
    {
        _previousButtons = GamepadButtons.None;
        _leftThumbPressedAt = null;
        _rightThumbPressedAt = null;
        _previousGyroChord = false;
        _gyroToggledOn = false;
        SetGyroEnabled(false);
    }

    private void SetGyroEnabled(bool enabled)
    {
        if (IsGyroEnabled == enabled) return;
        IsGyroEnabled = enabled;
        GyroStateChanged?.Invoke(enabled);
    }
}
