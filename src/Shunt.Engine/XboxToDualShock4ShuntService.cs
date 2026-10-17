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
    private bool _leftStickTiltActive;

    public GyroActivatorMode ActivationMode { get; set; } = GyroActivatorMode.Toggle;
    public bool IsGyroEnabled { get; private set; }
    public event Action<bool>? GyroStateChanged;
    public event Action<string>? TouchpadActionChanged;

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
        var outputLeftStick = input.LeftStick;

        if ((buttons & GamepadButtons.Back) != 0 &&
            MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y) >= 0.30f)
            outputRightStick = StickPosition.Center;

        var backDown = (buttons & GamepadButtons.Back) != 0;
        var leftStickMag = MathF.Sqrt(input.LeftStick.X * input.LeftStick.X + input.LeftStick.Y * input.LeftStick.Y);
        var rightStickMag = MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y);
        bool isLeftStickGyroActive = false;
        bool isRightStickGyroActive = false;

        // Activate gyro tilt whenever Back is held + Left Stick moves, or when gyro is enabled on standard Xbox controllers
        if (backDown && leftStickMag >= 0.15f)
        {
            isLeftStickGyroActive = true;
            _backHadPartner = true;
            outputLeftStick = StickPosition.Center;
            if (!_leftStickTiltActive)
            {
                _leftStickTiltActive = true;
                TouchpadActionChanged?.Invoke("Left stick gyro tilt");
            }
        }
        else
        {
            _leftStickTiltActive = false;
            if (IsGyroEnabled && imuSample == null)
            {
                if (leftStickMag >= 0.08f)
                {
                    isLeftStickGyroActive = true;
                    outputLeftStick = StickPosition.Center;
                }
                else if (rightStickMag >= 0.08f)
                {
                    isRightStickGyroActive = true;
                    outputRightStick = StickPosition.Center;
                }
            }
        }

        float pitchAngle = 0f;
        float rollAngle = 0f;
        float gyroVelX = 0f;
        float gyroVelY = 0f;
        float gyroVelZ = 0f;
        float accelX = 0f;
        float accelY = 0f;
        float accelZ = 1.0f;

        if (isLeftStickGyroActive)
        {
            pitchAngle = input.LeftStick.Y * 45f;
            rollAngle = input.LeftStick.X * 45f;
            float pitchRad = pitchAngle * (MathF.PI / 180f);
            float rollRad = rollAngle * (MathF.PI / 180f);

            accelX = MathF.Sin(rollRad) * MathF.Cos(pitchRad);
            accelY = -MathF.Sin(pitchRad);
            accelZ = MathF.Cos(rollRad) * MathF.Cos(pitchRad);

            gyroVelX = input.LeftStick.Y * 180f;
            gyroVelY = input.LeftStick.X * 180f;
            gyroVelZ = input.LeftStick.X * 180f;
        }
        else if (isRightStickGyroActive)
        {
            pitchAngle = input.RightStick.Y * 45f;
            rollAngle = input.RightStick.X * 45f;
            float pitchRad = pitchAngle * (MathF.PI / 180f);
            float rollRad = rollAngle * (MathF.PI / 180f);

            accelX = MathF.Sin(rollRad) * MathF.Cos(pitchRad);
            accelY = -MathF.Sin(pitchRad);
            accelZ = MathF.Cos(rollRad) * MathF.Cos(pitchRad);

            gyroVelX = input.RightStick.Y * 180f;
            gyroVelY = input.RightStick.X * 180f;
            gyroVelZ = input.RightStick.X * 180f;
        }
        else if (imuSample is { } sample)
        {
            gyroVelX = IsGyroEnabled ? sample.GyroX : 0f;
            // Vendor report order is pitch, roll, yaw; DS4 expects pitch, yaw, roll.
            gyroVelY = IsGyroEnabled ? sample.GyroZ : 0f;
            gyroVelZ = IsGyroEnabled ? sample.GyroY : 0f;
            accelX = sample.AccelX;
            accelY = sample.AccelY;
            accelZ = sample.AccelZ;
            pitchAngle = MathF.Atan2(-accelY, MathF.Sqrt(accelX * accelX + accelZ * accelZ)) * (180f / MathF.PI);
            rollAngle = MathF.Atan2(accelX, accelZ == 0 ? 0.001f : accelZ) * (180f / MathF.PI);
        }
        else
        {
            gyroVelX = IsGyroEnabled ? input.GyroVelX : 0f;
            gyroVelY = IsGyroEnabled ? input.GyroVelY : 0f;
            gyroVelZ = IsGyroEnabled ? input.GyroVelZ : 0f;
            accelX = input.AccelX;
            accelY = input.AccelY;
            accelZ = input.AccelZ == 0f ? 1.0f : input.AccelZ;
        }

        _previousGyroChord = chordFormed;
        _previousButtons = buttons;

        return input with
        {
            Buttons = outputButtons,
            LeftStick = outputLeftStick,
            RightStick = outputRightStick,
            GyroPitch = pitchAngle,
            GyroRoll = rollAngle,
            GyroYaw = 0f,
            GyroVelX = gyroVelX,
            GyroVelY = gyroVelY,
            GyroVelZ = gyroVelZ,
            AccelX = accelX,
            AccelY = accelY,
            AccelZ = accelZ
        };
    }

    public void Reset(bool defaultGyroEnabled = false)
    {
        _previousButtons = GamepadButtons.None;
        _leftThumbPressedAt = null;
        _rightThumbPressedAt = null;
        _previousGyroChord = false;
        _gyroToggledOn = defaultGyroEnabled;
        _backHadPartner = false;
        _activeSwipe = GamepadButtons.None;
        _leftStickTiltActive = false;
        SetGyroEnabled(defaultGyroEnabled);
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
                var newFaces = faceButtons & ~(backWasDown ? _previousButtons : GamepadButtons.None);
                if ((newFaces & GamepadButtons.A) != 0) { outputButtons |= GamepadButtons.TouchpadTapBottom; TouchpadActionChanged?.Invoke("Bottom click"); }
                if ((newFaces & GamepadButtons.B) != 0) { outputButtons |= GamepadButtons.TouchpadTapRight; TouchpadActionChanged?.Invoke("Right click"); }
                if ((newFaces & GamepadButtons.X) != 0) { outputButtons |= GamepadButtons.TouchpadTapLeft; TouchpadActionChanged?.Invoke("Left click"); }
                if ((newFaces & GamepadButtons.Y) != 0) { outputButtons |= GamepadButtons.TouchpadTapTop; TouchpadActionChanged?.Invoke("Top click"); }
            }

            var magnitude = MathF.Sqrt(input.RightStick.X * input.RightStick.X + input.RightStick.Y * input.RightStick.Y);
            if (magnitude >= 0.70f)
            {
                _backHadPartner = true;
                outputButtons &= ~(GamepadButtons.SwipeUp | GamepadButtons.SwipeDown | GamepadButtons.SwipeLeft | GamepadButtons.SwipeRight);
                var previousSwipe = _activeSwipe;
                _activeSwipe = input.RightStick.X switch
                {
                    var x when MathF.Abs(x) >= MathF.Abs(input.RightStick.Y) && x > 0f => GamepadButtons.SwipeRight,
                    var x when MathF.Abs(x) >= MathF.Abs(input.RightStick.Y) && x < 0f => GamepadButtons.SwipeLeft,
                    _ when input.RightStick.Y > 0f => GamepadButtons.SwipeUp,
                    _ => GamepadButtons.SwipeDown
                };
                outputButtons |= _activeSwipe;
                if (_activeSwipe != previousSwipe)
                    TouchpadActionChanged?.Invoke(_activeSwipe switch
                    {
                        GamepadButtons.SwipeUp => "Swipe up",
                        GamepadButtons.SwipeDown => "Swipe down",
                        GamepadButtons.SwipeLeft => "Swipe left",
                        _ => "Swipe right"
                    });
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
            if (!_backHadPartner)
            {
                outputButtons |= GamepadButtons.TouchpadTap;
                TouchpadActionChanged?.Invoke("Center click");
            }
            _backHadPartner = false;
            _activeSwipe = GamepadButtons.None;
        }
        else
        {
            _activeSwipe = GamepadButtons.None;
        }

        return outputButtons;
    }

    public void SetGyroEnabled(bool enabled)
    {
        if (IsGyroEnabled == enabled) return;
        IsGyroEnabled = enabled;
        _gyroToggledOn = enabled;
        GyroStateChanged?.Invoke(enabled);
    }
}
