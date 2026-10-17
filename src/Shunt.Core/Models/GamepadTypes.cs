using System;

namespace Shunt.Core.Models;

/// <summary>
/// Bitflag enumeration of standardized gamepad buttons.
/// </summary>
[Flags]
public enum GamepadButtons : ulong
{
    None = 0,
    DpadUp = 1UL << 0,
    DpadDown = 1UL << 1,
    DpadLeft = 1UL << 2,
    DpadRight = 1UL << 3,
    Start = 1UL << 4,
    Back = 1UL << 5,
    LeftThumb = 1UL << 6,
    RightThumb = 1UL << 7,
    LeftShoulder = 1UL << 8,
    RightShoulder = 1UL << 9,
    Guide = 1UL << 10,
    A = 1UL << 12,
    B = 1UL << 13,
    X = 1UL << 14,
    Y = 1UL << 15,
    Touchpad = 1UL << 16,
    Share = 1UL << 17,
    Options = 1UL << 18,
    // DualShock 4 Touchpad Gestures
    TouchpadClick = 1UL << 19,
    TouchpadTap = 1UL << 20,
    SwipeUp = 1UL << 21,
    SwipeDown = 1UL << 22,
    SwipeLeft = 1UL << 23,
    SwipeRight = 1UL << 24,
    // Motion & Gyroscope
    TiltLeft = 1UL << 25,
    TiltRight = 1UL << 26,
    TiltUp = 1UL << 27,
    TiltDown = 1UL << 28,
    VGYRO = 1UL << 29,
    // Paddles (Xbox Elite / DualSense Edge)
    PaddleP1 = 1UL << 30,
    PaddleP2 = 1UL << 31,
    PaddleP3 = 1UL << 32,
    PaddleP4 = 1UL << 33,
    TouchpadTapLeft = 1UL << 34,
    TouchpadTapRight = 1UL << 35,
    TouchpadTapTop = 1UL << 36,
    TouchpadTapBottom = 1UL << 37
}

/// <summary>
/// Supported virtual gamepad emulation targets (plan 07 §2: Xbox 360, Xbox One,
/// DualShock 4, DualShock 3, Switch Pro, Keyboard &amp; Mouse).
/// </summary>
public enum VirtualGamepadType
{
    Xbox360,
    DualShock4,
    XboxOne,
    DualShock3,
    SwitchPro,
    KeyboardAndMouse
}

/// <summary>
/// 2D stick coordinate normalized from -1.0 to +1.0.
/// </summary>
public readonly record struct StickPosition(float X, float Y)
{
    public static readonly StickPosition Center = new(0f, 0f);

    public short ToShortX() => (short)Math.Clamp(X * 32767f, -32768f, 32767f);
    public short ToShortY() => (short)Math.Clamp(Y * 32767f, -32768f, 32767f);

    public byte ToByteX() => (byte)Math.Clamp((X + 1.0f) * 127.5f, 0f, 255f);
    public byte ToByteY() => (byte)Math.Clamp((-Y + 1.0f) * 127.5f, 0f, 255f);
}

/// <summary>
/// Snapshot of a controller's complete state.
/// </summary>
public record GamepadState
{
    public GamepadButtons Buttons { get; init; } = GamepadButtons.None;
    public StickPosition LeftStick { get; init; } = StickPosition.Center;
    public StickPosition RightStick { get; init; } = StickPosition.Center;
    public float LeftTrigger { get; init; } = 0f;  // 0.0 to 1.0
    public float RightTrigger { get; init; } = 0f; // 0.0 to 1.0
    public float GyroPitch { get; init; } = 0f;    // Tilt forward/back in degrees (-90 to +90)
    public float GyroRoll { get; init; } = 0f;     // Lean left/right in degrees (-180 to +180)
    public float GyroYaw { get; init; } = 0f;      // Compass heading in degrees (-180 to +180)
    public float GyroVelX { get; init; } = 0f;     // Pitch velocity (deg/s)
    public float GyroVelY { get; init; } = 0f;     // Yaw velocity (deg/s)
    public float GyroVelZ { get; init; } = 0f;     // Roll velocity (deg/s)
    public float AccelX { get; init; } = 0f;       // Linear acceleration (g)
    public float AccelY { get; init; } = 0f;
    public float AccelZ { get; init; } = 0f;

    public bool HasButton(GamepadButtons button) => (Buttons & button) == button;

    public GamepadState WithButton(GamepadButtons button, bool pressed)
    {
        var newButtons = pressed ? (Buttons | button) : (Buttons & ~button);
        return this with { Buttons = newButtons };
    }
}
