using System;
using Shunt.Core.Models;

namespace Shunt.Emulation;

/// <summary>
/// Converts mouse deltas or gyro tilt into 6-axis IMU reports consumable by
/// Virtual DualShock 4 / Switch Pro targets (plan 07 §6: VirtualMotionSynthesizer).
/// </summary>
public sealed class VirtualMotionSynthesizer
{
    /// <summary>Accumulated orientation in degrees.</summary>
    public float Pitch { get; private set; }
    public float Roll { get; private set; }
    public float Yaw { get; private set; }

    /// <summary>Sensitivity scaling from input units to degrees.</summary>
    public float MouseScale { get; set; } = 0.08f;
    public float GyroScale { get; set; } = 1.0f;

    // Low-pass filtered angular velocities (deg/s)
    private float _velX, _velY, _velZ;

    /// <summary>
    /// Feeds a mouse delta (already ballistics-processed) and derives virtual IMU motion.
    /// Mouse X drives Yaw, mouse Y drives Pitch.
    /// </summary>
    public MotionState FeedMouseDelta(int deltaX, int deltaY, float deltaTimeSeconds)
    {
        float dt = MathF.Max(deltaTimeSeconds, 1f / 1000f);
        float yawDeg = deltaX * MouseScale;
        float pitchDeg = deltaY * MouseScale;

        Yaw = WrapAngle(Yaw + yawDeg);
        Pitch = Math.Clamp(Pitch + pitchDeg, -90f, 90f);

        _velX = Lerp(_velX, pitchDeg / dt, 0.5f);
        _velY = Lerp(_velY, yawDeg / dt, 0.5f);
        _velZ = Lerp(_velZ, 0f, 0.2f);

        return BuildState();
    }

    /// <summary>
    /// Feeds raw physical gyroscope angular velocities (deg/s) and produces the emulated IMU snapshot.
    /// </summary>
    public MotionState FeedGyroVelocity(float gyroX, float gyroY, float gyroZ, float deltaTimeSeconds)
    {
        float dt = MathF.Max(deltaTimeSeconds, 1f / 1000f);
        Pitch = Math.Clamp(WrapAngle(Pitch + gyroX * GyroScale * dt), -90f, 90f);
        Roll = Math.Clamp(WrapAngle(Roll + gyroZ * GyroScale * dt), -180f, 180f);
        Yaw = WrapAngle(Yaw + gyroY * GyroScale * dt);

        _velX = Lerp(_velX, gyroX * GyroScale, 0.5f);
        _velY = Lerp(_velY, gyroY * GyroScale, 0.5f);
        _velZ = Lerp(_velZ, gyroZ * GyroScale, 0.5f);

        return BuildState();
    }

    /// <summary>
    /// Feeds a device tilt pose in degrees (from accelerometer-derived attitude) directly.
    /// </summary>
    public MotionState FeedTilt(float pitch, float roll)
    {
        Pitch = pitch;
        Roll = roll;
        _velX = Lerp(_velX, 0f, 0.2f);
        _velY = Lerp(_velY, 0f, 0.2f);
        _velZ = Lerp(_velZ, 0f, 0.2f);
        return BuildState();
    }

    /// <summary>
    /// Resets the emulated sensor orientation (RewasdUserCommand.VirtualGyroReset).
    /// </summary>
    public void Reset()
    {
        Pitch = Roll = Yaw = 0f;
        _velX = _velY = _velZ = 0f;
    }

    private MotionState BuildState()
    {
        // Gravity-projected accelerometer in Gs from orientation
        float pitchRad = Pitch * MathF.PI / 180f;
        float rollRad = Roll * MathF.PI / 180f;
        float accelX = MathF.Sin(rollRad) * -MathF.Sin(pitchRad);
        float accelY = MathF.Cos(rollRad) * MathF.Sin(pitchRad);
        float accelZ = MathF.Cos(pitchRad) * MathF.Cos(rollRad);

        return new MotionState(
            Pitch, Roll, Yaw,
            accelX, accelY, accelZ,
            _velX, _velY, _velZ);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float WrapAngle(float degrees)
    {
        float d = degrees % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }
}

/// <summary>
/// Bridges the legacy 32-bit pipeline <see cref="GamepadState"/> to the high-capacity
/// <see cref="VirtualGamepadState"/> (plan 07 §3) and back.
/// </summary>
public static class VirtualGamepadStateBridge
{
    /// <summary>Upgrades a pipeline state into the high-capacity virtual state.</summary>
    public static VirtualGamepadState ToVirtual(GamepadState state)
    {
        var buttons = GamepadDigitalButtons.None;
        var src = state.Buttons;

        if (src.HasFlag(GamepadButtons.A)) buttons |= GamepadDigitalButtons.A;
        if (src.HasFlag(GamepadButtons.B)) buttons |= GamepadDigitalButtons.B;
        if (src.HasFlag(GamepadButtons.X)) buttons |= GamepadDigitalButtons.X;
        if (src.HasFlag(GamepadButtons.Y)) buttons |= GamepadDigitalButtons.Y;
        if (src.HasFlag(GamepadButtons.LeftShoulder)) buttons |= GamepadDigitalButtons.LeftBumper;
        if (src.HasFlag(GamepadButtons.RightShoulder)) buttons |= GamepadDigitalButtons.RightBumper;
        if (src.HasFlag(GamepadButtons.LeftThumb)) buttons |= GamepadDigitalButtons.LeftThumbClick;
        if (src.HasFlag(GamepadButtons.RightThumb)) buttons |= GamepadDigitalButtons.RightThumbClick;
        if (src.HasFlag(GamepadButtons.Start)) buttons |= GamepadDigitalButtons.Start;
        if (src.HasFlag(GamepadButtons.Back)) buttons |= GamepadDigitalButtons.Back;
        if (src.HasFlag(GamepadButtons.Guide)) buttons |= GamepadDigitalButtons.Guide;
        if (src.HasFlag(GamepadButtons.DpadUp)) buttons |= GamepadDigitalButtons.DpadUp;
        if (src.HasFlag(GamepadButtons.DpadDown)) buttons |= GamepadDigitalButtons.DpadDown;
        if (src.HasFlag(GamepadButtons.DpadLeft)) buttons |= GamepadDigitalButtons.DpadLeft;
        if (src.HasFlag(GamepadButtons.DpadRight)) buttons |= GamepadDigitalButtons.DpadRight;
        if (src.HasFlag(GamepadButtons.Touchpad)) buttons |= GamepadDigitalButtons.TouchpadClick;
        if (src.HasFlag(GamepadButtons.TouchpadClick)) buttons |= GamepadDigitalButtons.TouchpadClick;
        if (src.HasFlag(GamepadButtons.TouchpadTap)) buttons |= GamepadDigitalButtons.TouchpadTapCenter;
        if (src.HasFlag(GamepadButtons.Share)) buttons |= GamepadDigitalButtons.Capture;
        if (src.HasFlag(GamepadButtons.Options)) buttons |= GamepadDigitalButtons.Start;
        if (src.HasFlag(GamepadButtons.SwipeUp)) buttons |= GamepadDigitalButtons.TouchpadSwipeUp;
        if (src.HasFlag(GamepadButtons.SwipeDown)) buttons |= GamepadDigitalButtons.TouchpadSwipeDown;
        if (src.HasFlag(GamepadButtons.SwipeLeft)) buttons |= GamepadDigitalButtons.TouchpadSwipeLeft;
        if (src.HasFlag(GamepadButtons.SwipeRight)) buttons |= GamepadDigitalButtons.TouchpadSwipeRight;
        if (src.HasFlag(GamepadButtons.TiltUp)) buttons |= GamepadDigitalButtons.TouchpadSwipeUp;
        if (src.HasFlag(GamepadButtons.TiltDown)) buttons |= GamepadDigitalButtons.TouchpadSwipeDown;
        if (src.HasFlag(GamepadButtons.TiltLeft)) buttons |= GamepadDigitalButtons.TouchpadSwipeLeft;
        if (src.HasFlag(GamepadButtons.TiltRight)) buttons |= GamepadDigitalButtons.TouchpadSwipeRight;

        return new VirtualGamepadState
        {
            Buttons = buttons,
            LeftStick = state.LeftStick,
            RightStick = state.RightStick,
            LeftTrigger = state.LeftTrigger,
            RightTrigger = state.RightTrigger,
            Motion = new MotionState(
                state.GyroPitch, state.GyroRoll, state.GyroYaw,
                state.AccelX, state.AccelY, state.AccelZ,
                state.GyroVelX, state.GyroVelY, state.GyroVelZ)
        };
    }

    /// <summary>Downgrades a high-capacity virtual state into the pipeline state (lossy for extended inputs).</summary>
    public static GamepadState ToPipeline(VirtualGamepadState state)
    {
        var buttons = GamepadButtons.None;
        var src = state.Buttons;

        if (src.HasFlag(GamepadDigitalButtons.A)) buttons |= GamepadButtons.A;
        if (src.HasFlag(GamepadDigitalButtons.B)) buttons |= GamepadButtons.B;
        if (src.HasFlag(GamepadDigitalButtons.X)) buttons |= GamepadButtons.X;
        if (src.HasFlag(GamepadDigitalButtons.Y)) buttons |= GamepadButtons.Y;
        if (src.HasFlag(GamepadDigitalButtons.LeftBumper)) buttons |= GamepadButtons.LeftShoulder;
        if (src.HasFlag(GamepadDigitalButtons.RightBumper)) buttons |= GamepadButtons.RightShoulder;
        if (src.HasFlag(GamepadDigitalButtons.LeftThumbClick)) buttons |= GamepadButtons.LeftThumb;
        if (src.HasFlag(GamepadDigitalButtons.RightThumbClick)) buttons |= GamepadButtons.RightThumb;
        if (src.HasFlag(GamepadDigitalButtons.Start)) buttons |= GamepadButtons.Start;
        if (src.HasFlag(GamepadDigitalButtons.Back)) buttons |= GamepadButtons.Back;
        if (src.HasFlag(GamepadDigitalButtons.Guide)) buttons |= GamepadButtons.Guide;
        if (src.HasFlag(GamepadDigitalButtons.Capture)) buttons |= GamepadButtons.Share;
        if (src.HasFlag(GamepadDigitalButtons.DpadUp)) buttons |= GamepadButtons.DpadUp;
        if (src.HasFlag(GamepadDigitalButtons.DpadDown)) buttons |= GamepadButtons.DpadDown;
        if (src.HasFlag(GamepadDigitalButtons.DpadLeft)) buttons |= GamepadButtons.DpadLeft;
        if (src.HasFlag(GamepadDigitalButtons.DpadRight)) buttons |= GamepadButtons.DpadRight;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadClick)) buttons |= GamepadButtons.TouchpadClick;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadTapCenter)) buttons |= GamepadButtons.TouchpadTap;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadSwipeUp)) buttons |= GamepadButtons.SwipeUp;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadSwipeDown)) buttons |= GamepadButtons.SwipeDown;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadSwipeLeft)) buttons |= GamepadButtons.SwipeLeft;
        if (src.HasFlag(GamepadDigitalButtons.TouchpadSwipeRight)) buttons |= GamepadButtons.SwipeRight;

        return new GamepadState
        {
            Buttons = buttons,
            LeftStick = state.LeftStick,
            RightStick = state.RightStick,
            LeftTrigger = state.LeftTrigger,
            RightTrigger = state.RightTrigger,
            GyroPitch = state.Motion.Pitch,
            GyroRoll = state.Motion.Roll,
            GyroYaw = state.Motion.Yaw,
            GyroVelX = state.Motion.GyroX,
            GyroVelY = state.Motion.GyroY,
            GyroVelZ = state.Motion.GyroZ
        };
    }
}
