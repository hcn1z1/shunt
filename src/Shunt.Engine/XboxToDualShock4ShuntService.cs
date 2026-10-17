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
    private bool _backHadPartner;
    private GamepadButtons _activeSwipe;

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

        outputButtons = ResolveTouchpadChords(input, outputButtons);
        var outputRightStick = input.RightStick;
        if ((buttons & GamepadButtons.Back) != 0 &&
            MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y) >= 0.30f)
            outputRightStick = StickPosition.Center;
        _previousGyroChord = chordFormed;
        _previousButtons = buttons;

        if (imuSample is { } sample)
        {
            return input with
            {
                Buttons = outputButtons,
                RightStick = outputRightStick,
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
            RightStick = outputRightStick,
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
        _backHadPartner = false;
        _activeSwipe = GamepadButtons.None;
        SetGyroEnabled(false);
    }

    private GamepadButtons ResolveTouchpadChords(GamepadState input, GamepadButtons outputButtons)
    {
        var back = (input.Buttons & GamepadButtons.Back) != 0;
        var backWasDown = (_previousButtons & GamepadButtons.Back) != 0;
        var faceButtons = input.Buttons & (GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y);
        if (back && !backWasDown) _backHadPartner = false;

        if (back)
        {
            outputButtons &= ~GamepadButtons.Back;
            if (faceButtons != GamepadButtons.None)
            {
                _backHadPartner = true;
                outputButtons &= ~faceButtons;
                if ((faceButtons & GamepadButtons.A) != 0) outputButtons |= GamepadButtons.TouchpadTapBottom;
                if ((faceButtons & GamepadButtons.B) != 0) outputButtons |= GamepadButtons.TouchpadTapRight;
                if ((faceButtons & GamepadButtons.X) != 0) outputButtons |= GamepadButtons.TouchpadTapLeft;
                if ((faceButtons & GamepadButtons.Y) != 0) outputButtons |= GamepadButtons.TouchpadTapTop;
            }

            var magnitude = MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y);
            if (magnitude >= 0.70f)
            {
                _backHadPartner = true;
                outputButtons &= ~(GamepadButtons.SwipeUp | GamepadButtons.SwipeDown | GamepadButtons.SwipeLeft | GamepadButtons.SwipeRight);
                _activeSwipe = input.RightStick.X switch
                {
                    var x when MathF.Abs(x) >= MathF.Abs(input.RightStick.Y) && x > 0f => GamepadButtons.SwipeRight,
                    var x when MathF.Abs(x) >= MathF.Abs(input.RightStick.Y) && x < 0f => GamepadButtons.SwipeLeft,
                    _ when input.RightStick.Y > 0f => GamepadButtons.SwipeUp,
                    _ => GamepadButtons.SwipeDown
                };
                outputButtons |= _activeSwipe;
            }
            else if (magnitude < 0.30f)
            {
                outputButtons &= ~(GamepadButtons.SwipeUp | GamepadButtons.SwipeDown | GamepadButtons.SwipeLeft | GamepadButtons.SwipeRight);
                _activeSwipe = GamepadButtons.None;
            }
            else
            {
                outputButtons &= ~(GamepadButtons.SwipeUp | GamepadButtons.SwipeDown | GamepadButtons.SwipeLeft | GamepadButtons.SwipeRight);
                outputButtons |= _activeSwipe;
            }
        }
        else if (backWasDown)
        {
            outputButtons &= ~GamepadButtons.Back;
            if (!_backHadPartner) outputButtons |= GamepadButtons.TouchpadTap;
            _backHadPartner = false;
            _activeSwipe = GamepadButtons.None;
        }
        else
        {
            _activeSwipe = GamepadButtons.None;
        }

        return outputButtons;
    }

    private void SetGyroEnabled(bool enabled)
    {
        if (IsGyroEnabled == enabled) return;
        IsGyroEnabled = enabled;
        GyroStateChanged?.Invoke(enabled);
    }
}
