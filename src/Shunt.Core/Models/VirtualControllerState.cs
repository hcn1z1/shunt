using System;

namespace Shunt.Core.Models;

/// <summary>
/// Expanded 64-bit digital button mask for core gamepad inputs (plan 07 §3).
/// Tracks 256+ distinct button IDs including paddles, extended face buttons, grips, and touchpad gestures.
/// </summary>
[Flags]
public enum GamepadDigitalButtons : ulong
{
    None = 0,
    // Face Buttons
    A = 1UL << 0,
    B = 1UL << 1,
    X = 1UL << 2,
    Y = 1UL << 3,
    // Bumpers
    LeftBumper = 1UL << 4,
    RightBumper = 1UL << 5,
    // Clicks & System
    LeftThumbClick = 1UL << 6,
    RightThumbClick = 1UL << 7,
    Start = 1UL << 8,
    Back = 1UL << 9,
    Guide = 1UL << 10,
    Capture = 1UL << 11,
    TouchpadClick = 1UL << 12,
    Assistant = 1UL << 13,
    MicMute = 1UL << 14,
    // D-Pad
    DpadUp = 1UL << 16,
    DpadDown = 1UL << 17,
    DpadLeft = 1UL << 18,
    DpadRight = 1UL << 19,
    // Elite / Rear Paddles
    PaddleP1 = 1UL << 20, // Upper Left
    PaddleP2 = 1UL << 21, // Upper Right
    PaddleP3 = 1UL << 22, // Lower Left
    PaddleP4 = 1UL << 23, // Lower Right
    // Flydigi / Extended Face Buttons
    ButtonC = 1UL << 24,
    ButtonZ = 1UL << 25,
    ButtonM1 = 1UL << 26,
    ButtonM2 = 1UL << 27,
    ButtonM3 = 1UL << 28,
    ButtonM4 = 1UL << 29,
    // Steam Deck Grips
    GripL4 = 1UL << 30,
    GripL5 = 1UL << 31,
    GripR4 = 1UL << 32,
    GripR5 = 1UL << 33,
    // Trackpad Clicks & Taps
    Trackpad1Click = 1UL << 34,
    Trackpad1Tap = 1UL << 35,
    Trackpad2Click = 1UL << 36,
    Trackpad2Tap = 1UL << 37,
    // Virtual DS4 Touchpad Digital Events
    TouchpadTapLeft = 1UL << 40,
    TouchpadTapRight = 1UL << 41,
    TouchpadTapCenter = 1UL << 42,
    TouchpadMultiTap = 1UL << 43,
    TouchpadSwipeUp = 1UL << 44,
    TouchpadSwipeDown = 1UL << 45,
    TouchpadSwipeLeft = 1UL << 46,
    TouchpadSwipeRight = 1UL << 47,
    TouchpadZoomIn = 1UL << 48,
    TouchpadZoomOut = 1UL << 49
}

/// <summary>
/// Capacitive multi-touch point for DualShock 4 and Steam Deck trackpads (plan 07 §3).
/// </summary>
public readonly record struct TouchPoint(bool IsActive, byte Id, ushort X, ushort Y);

/// <summary>
/// Complete 6-DOF IMU motion snapshot (plan 07 §3).
/// </summary>
public readonly record struct MotionState(
    float Pitch, float Roll, float Yaw,
    float AccelX, float AccelY, float AccelZ,
    float GyroX, float GyroY, float GyroZ);

/// <summary>
/// Complete synthesized state of any virtual controller (plan 07 §3).
/// </summary>
public record VirtualGamepadState
{
    public GamepadDigitalButtons Buttons { get; init; } = GamepadDigitalButtons.None;

    // Sticks (-1.0f to +1.0f)
    public StickPosition LeftStick { get; init; } = StickPosition.Center;
    public StickPosition RightStick { get; init; } = StickPosition.Center;
    public StickPosition AdditionalStick { get; init; } = StickPosition.Center;

    // Triggers (0.0f to 1.0f)
    public float LeftTrigger { get; init; } = 0f;
    public float RightTrigger { get; init; } = 0f;

    // DS3 Pressure-Sensitive Analog Face Buttons (0.0f to 1.0f)
    public float PressureCross { get; init; } = 0f;
    public float PressureCircle { get; init; } = 0f;
    public float PressureSquare { get; init; } = 0f;
    public float PressureTriangle { get; init; } = 0f;
    public float PressureDpadUp { get; init; } = 0f;
    public float PressureDpadDown { get; init; } = 0f;
    public float PressureDpadLeft { get; init; } = 0f;
    public float PressureDpadRight { get; init; } = 0f;

    // DualShock 4 Touchpad Fingers
    public TouchPoint Touch0 { get; init; } = default;
    public TouchPoint Touch1 { get; init; } = default;

    // 6-Axis Motion & IMU
    public MotionState Motion { get; init; } = default;

    public bool HasButton(GamepadDigitalButtons button) => (Buttons & button) == button;
}

/// <summary>
/// Supported virtual gamepad emulation targets (plan 07 §2).
/// Numeric values match reWASD internal enum (`Ds4` 0, `The360` 1, `XboxOneBT` 2,
/// `NintendoSwitchPro` 3, `Ds3` 4, `KeyboardAndMouse` 5).
/// </summary>
public enum VirtualControllerTarget
{
    Ds4 = 0,
    The360 = 1,
    XboxOneBT = 2,
    NintendoSwitchPro = 3,
    Ds3 = 4,
    KeyboardAndMouse = 5
}

/// <summary>
/// Maps between the legacy <see cref="VirtualGamepadType"/> used by the pipeline and the
/// full reWASD virtual target enumeration.
/// </summary>
public static class VirtualTargetMapper
{
    public static VirtualControllerTarget ToTarget(VirtualGamepadType type) => type switch
    {
        VirtualGamepadType.Xbox360 => VirtualControllerTarget.The360,
        VirtualGamepadType.DualShock4 => VirtualControllerTarget.Ds4,
        VirtualGamepadType.XboxOne => VirtualControllerTarget.XboxOneBT,
        VirtualGamepadType.DualShock3 => VirtualControllerTarget.Ds3,
        VirtualGamepadType.SwitchPro => VirtualControllerTarget.NintendoSwitchPro,
        VirtualGamepadType.KeyboardAndMouse => VirtualControllerTarget.KeyboardAndMouse,
        _ => VirtualControllerTarget.The360
    };

    public static VirtualGamepadType ToGamepadType(VirtualControllerTarget target) => target switch
    {
        VirtualControllerTarget.Ds4 => VirtualGamepadType.DualShock4,
        VirtualControllerTarget.The360 => VirtualGamepadType.Xbox360,
        VirtualControllerTarget.XboxOneBT => VirtualGamepadType.XboxOne,
        VirtualControllerTarget.Ds3 => VirtualGamepadType.DualShock3,
        VirtualControllerTarget.NintendoSwitchPro => VirtualGamepadType.SwitchPro,
        VirtualControllerTarget.KeyboardAndMouse => VirtualGamepadType.KeyboardAndMouse,
        _ => VirtualGamepadType.Xbox360
    };
}
