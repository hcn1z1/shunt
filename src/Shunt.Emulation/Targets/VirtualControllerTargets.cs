using System;
using Shunt.Core.Models;

namespace Shunt.Emulation.Targets;

/// <summary>
/// A virtual controller target that formats the high-capacity <see cref="VirtualGamepadState"/>
/// into the device-specific HID output report (plan 07 §4).
/// </summary>
public interface IVirtualControllerTarget
{
    /// <summary>Virtual emulation target implemented by this formatter.</summary>
    VirtualControllerTarget Target { get; }

    /// <summary>Friendly device name advertised to the OS.</summary>
    string DeviceName { get; }

    /// <summary>
    /// Formats the synthesized state into the raw HID input report bytes
    /// that the virtual device publishes to the OS.
    /// </summary>
    byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0);
}

/// <summary>
/// Target 1: Virtual Xbox 360 — standard XInput GAMEPAD structure (plan 07 §4.1).
/// </summary>
public sealed class Xbox360Target : IVirtualControllerTarget
{
    public VirtualControllerTarget Target => VirtualControllerTarget.The360;
    public string DeviceName => "Virtual Xbox 360 Controller";

    public byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0)
    {
        var b = state.Buttons;
        // XINPUT_GAMEPAD: wButtons (2 bytes), bLeftTrigger, bRightTrigger, sThumbLX, sThumbLY, sThumbRX, sThumbRY
        ushort wButtons = 0;
        if (b.HasFlag(GamepadDigitalButtons.DpadUp)) wButtons |= 0x0001;
        if (b.HasFlag(GamepadDigitalButtons.DpadDown)) wButtons |= 0x0002;
        if (b.HasFlag(GamepadDigitalButtons.DpadLeft)) wButtons |= 0x0004;
        if (b.HasFlag(GamepadDigitalButtons.DpadRight)) wButtons |= 0x0008;
        if (b.HasFlag(GamepadDigitalButtons.Start)) wButtons |= 0x0010;
        if (b.HasFlag(GamepadDigitalButtons.Back)) wButtons |= 0x0020;
        if (b.HasFlag(GamepadDigitalButtons.LeftThumbClick)) wButtons |= 0x0040;
        if (b.HasFlag(GamepadDigitalButtons.RightThumbClick)) wButtons |= 0x0080;
        if (b.HasFlag(GamepadDigitalButtons.LeftBumper)) wButtons |= 0x0100;
        if (b.HasFlag(GamepadDigitalButtons.RightBumper)) wButtons |= 0x0200;
        if (b.HasFlag(GamepadDigitalButtons.Guide)) wButtons |= 0x0400;
        if (b.HasFlag(GamepadDigitalButtons.A)) wButtons |= 0x1000;
        if (b.HasFlag(GamepadDigitalButtons.B)) wButtons |= 0x2000;
        if (b.HasFlag(GamepadDigitalButtons.X)) wButtons |= 0x4000;
        if (b.HasFlag(GamepadDigitalButtons.Y)) wButtons |= 0x8000;

        var report = new byte[12];
        report[0] = (byte)(wButtons & 0xFF);
        report[1] = (byte)(wButtons >> 8);
        report[2] = (byte)(Math.Clamp(state.LeftTrigger, 0f, 1f) * 255f);
        report[3] = (byte)(Math.Clamp(state.RightTrigger, 0f, 1f) * 255f);
        BitConverter.GetBytes(state.LeftStick.ToShortX()).CopyTo(report, 4);
        BitConverter.GetBytes(state.LeftStick.ToShortY()).CopyTo(report, 6);
        BitConverter.GetBytes(state.RightStick.ToShortX()).CopyTo(report, 8);
        BitConverter.GetBytes(state.RightStick.ToShortY()).CopyTo(report, 10);
        return report;
    }
}

/// <summary>
/// Target 2: Virtual Xbox One — 4 rumble motors (impulse triggers) + Elite paddles P1-P4 +
/// Share button (plan 07 §4.2). Uses extended Xbox One HID descriptor layout.
/// </summary>
public sealed class XboxOneTarget : IVirtualControllerTarget
{
    public VirtualControllerTarget Target => VirtualControllerTarget.XboxOneBT;
    public string DeviceName => "Virtual Xbox One Controller";

    public byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0)
    {
        var b = state.Buttons;
        // Xbox One BT HID input report (GIP): buttons, triggers, sticks
        uint buttons = 0;
        if (b.HasFlag(GamepadDigitalButtons.A)) buttons |= 1 << 12;
        if (b.HasFlag(GamepadDigitalButtons.B)) buttons |= 1 << 13;
        if (b.HasFlag(GamepadDigitalButtons.X)) buttons |= 1 << 14;
        if (b.HasFlag(GamepadDigitalButtons.Y)) buttons |= 1 << 15;
        if (b.HasFlag(GamepadDigitalButtons.DpadUp)) buttons |= 1 << 0;
        if (b.HasFlag(GamepadDigitalButtons.DpadDown)) buttons |= 1 << 1;
        if (b.HasFlag(GamepadDigitalButtons.DpadLeft)) buttons |= 1 << 2;
        if (b.HasFlag(GamepadDigitalButtons.DpadRight)) buttons |= 1 << 3;
        if (b.HasFlag(GamepadDigitalButtons.LeftBumper)) buttons |= 1 << 4;
        if (b.HasFlag(GamepadDigitalButtons.RightBumper)) buttons |= 1 << 5;
        if (b.HasFlag(GamepadDigitalButtons.LeftThumbClick)) buttons |= 1 << 6;
        if (b.HasFlag(GamepadDigitalButtons.RightThumbClick)) buttons |= 1 << 7;
        if (b.HasFlag(GamepadDigitalButtons.Back)) buttons |= 1 << 8;   // View
        if (b.HasFlag(GamepadDigitalButtons.Start)) buttons |= 1 << 9;  // Menu
        if (b.HasFlag(GamepadDigitalButtons.Guide)) buttons |= 1 << 10; // Xbox
        if (b.HasFlag(GamepadDigitalButtons.Capture)) buttons |= 1 << 11; // Share
        // Elite paddles feed dedicated bits in the extended descriptor
        if (b.HasFlag(GamepadDigitalButtons.PaddleP1)) buttons |= 1 << 16;
        if (b.HasFlag(GamepadDigitalButtons.PaddleP2)) buttons |= 1 << 17;
        if (b.HasFlag(GamepadDigitalButtons.PaddleP3)) buttons |= 1 << 18;
        if (b.HasFlag(GamepadDigitalButtons.PaddleP4)) buttons |= 1 << 19;

        var report = new byte[18];
        report[0] = (byte)(buttons & 0xFF);
        report[1] = (byte)((buttons >> 8) & 0xFF);
        report[2] = (byte)((buttons >> 16) & 0xFF);
        report[3] = (byte)(Math.Clamp(state.LeftTrigger, 0f, 1f) * 1023f / 4f);
        report[4] = (byte)(Math.Clamp(state.RightTrigger, 0f, 1f) * 1023f / 4f);
        BitConverter.GetBytes(state.LeftStick.ToShortX()).CopyTo(report, 6);
        BitConverter.GetBytes(state.LeftStick.ToShortY()).CopyTo(report, 8);
        BitConverter.GetBytes(state.RightStick.ToShortX()).CopyTo(report, 10);
        BitConverter.GetBytes(state.RightStick.ToShortY()).CopyTo(report, 12);
        return report;
    }
}

/// <summary>
/// Target 3: Virtual DualShock 4 — touchpad multi-touch + 6-axis IMU (plan 07 §4.3).
/// </summary>
public sealed class DualShock4Target : IVirtualControllerTarget
{
    public VirtualControllerTarget Target => VirtualControllerTarget.Ds4;
    public string DeviceName => "Virtual DualShock 4 Controller";

    public byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0)
    {
        var b = state.Buttons;
        var report = new byte[64];
        report[0] = 0x01; // Report ID

        // Byte 1-2: buttons 1+2, byte 3: PS + touchpad click + counter
        ushort w = 0;
        if (b.HasFlag(GamepadDigitalButtons.A)) w |= 1 << 4;           // Cross
        if (b.HasFlag(GamepadDigitalButtons.B)) w |= 1 << 5;           // Circle
        if (b.HasFlag(GamepadDigitalButtons.X)) w |= 1 << 6;           // Square
        if (b.HasFlag(GamepadDigitalButtons.Y)) w |= 1 << 7;           // Triangle
        if (b.HasFlag(GamepadDigitalButtons.LeftBumper)) w |= 1 << 8;
        if (b.HasFlag(GamepadDigitalButtons.RightBumper)) w |= 1 << 9;
        if (b.HasFlag(GamepadDigitalButtons.Back)) w |= 1 << 12;       // Share
        if (b.HasFlag(GamepadDigitalButtons.Start)) w |= 1 << 13;      // Options
        if (b.HasFlag(GamepadDigitalButtons.LeftThumbClick)) w |= 1 << 14;
        if (b.HasFlag(GamepadDigitalButtons.RightThumbClick)) w |= 1 << 15;
        report[1] = (byte)(w & 0xFF);
        report[2] = (byte)(w >> 8);

        byte special = (byte)(reportCounter & 0x0F);
        if (b.HasFlag(GamepadDigitalButtons.Guide)) special |= 1 << 4;      // PS
        if (b.HasFlag(GamepadDigitalButtons.TouchpadClick)) special |= 1 << 5;
        report[3] = special;

        report[4] = ComputeDs4DPad(b);      // D-Pad hat + LT
        report[5] = (byte)(Math.Clamp(state.LeftTrigger, 0f, 1f) * 255f);
        report[6] = (byte)(Math.Clamp(state.RightTrigger, 0f, 1f) * 255f);

        // Sticks (0..255, center 128)
        report[7] = state.LeftStick.ToByteX();
        report[8] = state.LeftStick.ToByteY();
        report[9] = state.RightStick.ToByteX();
        report[10] = state.RightStick.ToByteY();

        // Touchpad data: packet counter + 2 finger slots (IsActive, Id, X 12-bit, Y 8-bit)
        ushort packet = (ushort)(reportCounter & 0x7F);
        WriteTouchSlot(report, 32, packet, state.Touch0);
        WriteTouchSlot(report, 37, (ushort)(packet | 0x80), state.Touch1);

        // 6-Axis IMU: gyro (deg/s * 16), accel (Gs * 8192)
        WriteShortBE(report, 15, (short)(state.Motion.GyroX * 16f));
        WriteShortBE(report, 17, (short)(state.Motion.GyroY * 16f));
        WriteShortBE(report, 19, (short)(state.Motion.GyroZ * 16f));
        WriteShortBE(report, 21, (short)(state.Motion.AccelX * 8192f));
        WriteShortBE(report, 23, (short)(state.Motion.AccelY * 8192f));
        WriteShortBE(report, 25, (short)(state.Motion.AccelZ * 8192f));

        return report;
    }

    internal static byte ComputeDs4DPad(GamepadDigitalButtons b)
    {
        bool up = b.HasFlag(GamepadDigitalButtons.DpadUp);
        bool down = b.HasFlag(GamepadDigitalButtons.DpadDown);
        bool left = b.HasFlag(GamepadDigitalButtons.DpadLeft);
        bool right = b.HasFlag(GamepadDigitalButtons.DpadRight);
        byte hat = (up, down, left, right) switch
        {
            (true, false, false, true) => 1,   // NE
            (true, false, true, false) => 7,   // NW
            (false, true, false, true) => 3,   // SE
            (false, true, true, false) => 5,   // SW
            (true, _, false, false) => 0,      // N
            (false, true, false, false) => 4,  // S
            (_, _, true, false) => 6,          // W
            (_, _, false, true) => 2,          // E
            _ => 8                             // Neutral
        };
        return hat;
    }

    private static void WriteTouchSlot(byte[] report, int offset, ushort counter, TouchPoint touch)
    {
        report[offset] = (byte)(counter & 0x7F);
        if (!touch.IsActive)
        {
            report[offset + 1] = 0x80; // finger released
            return;
        }
        report[offset + 1] = (byte)(0x7F & touch.Id);
        ushort x = Math.Clamp(touch.X, (ushort)0, (ushort)1919);
        byte y = (byte)Math.Clamp(touch.Y, (ushort)0, (ushort)942);
        report[offset + 2] = (byte)(x & 0xFF);
        report[offset + 3] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        report[offset + 4] = (byte)(y >> 4);
    }

    private static void WriteShortBE(byte[] report, int offset, short value)
    {
        report[offset] = (byte)((ushort)value >> 8);
        report[offset + 1] = (byte)((ushort)value & 0xFF);
    }
}

/// <summary>
/// Target 4: Virtual DualShock 3 — pressure-sensitive face buttons &amp; D-Pad (plan 07 §4.4).
/// Buttons report analog pressure bytes 0x00-0xFF.
/// </summary>
public sealed class DualShock3Target : IVirtualControllerTarget
{
    public VirtualControllerTarget Target => VirtualControllerTarget.Ds3;
    public string DeviceName => "Virtual DualShock 3 Controller";

    // Byte offsets of the pressure block in the DS3 input report 0x01.
    public const int PressureOffsetUp = 14;

    public byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0)
    {
        var b = state.Buttons;
        var report = new byte[49];
        report[0] = 0x01; // Report ID

        // Buttons: bytes 2-9 (bit columns: Select, L3, R3, Start / Up, Right, Down, Left / L2, R2, L1, R1 / Triangle, Circle, Cross, Square / PS)
        SetButton(report, 2, 0, b.HasFlag(GamepadDigitalButtons.Back));        // Select
        SetButton(report, 2, 1, b.HasFlag(GamepadDigitalButtons.LeftThumbClick));
        SetButton(report, 2, 2, b.HasFlag(GamepadDigitalButtons.RightThumbClick));
        SetButton(report, 2, 3, b.HasFlag(GamepadDigitalButtons.Start));
        SetButton(report, 3, 0, b.HasFlag(GamepadDigitalButtons.DpadUp));
        SetButton(report, 3, 1, b.HasFlag(GamepadDigitalButtons.DpadRight));
        SetButton(report, 3, 2, b.HasFlag(GamepadDigitalButtons.DpadDown));
        SetButton(report, 3, 3, b.HasFlag(GamepadDigitalButtons.DpadLeft));
        SetButton(report, 4, 2, b.HasFlag(GamepadDigitalButtons.LeftBumper));  // L1
        SetButton(report, 4, 3, b.HasFlag(GamepadDigitalButtons.RightBumper)); // R1
        SetButton(report, 5, 0, b.HasFlag(GamepadDigitalButtons.Y));           // Triangle
        SetButton(report, 5, 1, b.HasFlag(GamepadDigitalButtons.B));           // Circle
        SetButton(report, 5, 2, b.HasFlag(GamepadDigitalButtons.A));           // Cross
        SetButton(report, 5, 3, b.HasFlag(GamepadDigitalButtons.X));           // Square
        SetButton(report, 6, 0, b.HasFlag(GamepadDigitalButtons.Guide));       // PS

        // Pressure block (0x00-0xFF per button): Up, Right, Down, Left, L2, R2, L1, R1, Triangle, Circle, Cross, Square
        report[14] = PressureByte(state.PressureDpadUp, b.HasFlag(GamepadDigitalButtons.DpadUp));
        report[15] = PressureByte(state.PressureDpadRight, b.HasFlag(GamepadDigitalButtons.DpadRight));
        report[16] = PressureByte(state.PressureDpadDown, b.HasFlag(GamepadDigitalButtons.DpadDown));
        report[17] = PressureByte(state.PressureDpadLeft, b.HasFlag(GamepadDigitalButtons.DpadLeft));
        report[18] = (byte)(Math.Clamp(state.LeftTrigger, 0f, 1f) * 255f);  // L2
        report[19] = (byte)(Math.Clamp(state.RightTrigger, 0f, 1f) * 255f); // R2
        report[20] = (byte)(b.HasFlag(GamepadDigitalButtons.LeftBumper) ? 0xFF : 0x00);  // L1
        report[21] = (byte)(b.HasFlag(GamepadDigitalButtons.RightBumper) ? 0xFF : 0x00); // R1
        report[22] = PressureByte(state.PressureTriangle, b.HasFlag(GamepadDigitalButtons.Y));
        report[23] = PressureByte(state.PressureCircle, b.HasFlag(GamepadDigitalButtons.B));
        report[24] = PressureByte(state.PressureCross, b.HasFlag(GamepadDigitalButtons.A));
        report[25] = PressureByte(state.PressureSquare, b.HasFlag(GamepadDigitalButtons.X));

        // Sticks (0..255, center 128)
        report[6] = state.LeftStick.ToByteX();
        report[7] = state.LeftStick.ToByteY();
        report[8] = state.RightStick.ToByteX();
        report[9] = state.RightStick.ToByteY();

        // Sixaxis: X/Y accelerometer big-endian, Z gyro
        report[41] = (byte)(Math.Clamp(state.Motion.AccelX, -1f, 1f) * 127f + 128f);
        report[42] = (byte)(Math.Clamp(state.Motion.AccelY, -1f, 1f) * 127f + 128f);
        WriteShortBE(report, 46, (short)(state.Motion.GyroZ * 16f));

        return report;
    }

    internal static byte PressureByte(float pressure, bool digitalPressed)
    {
        if (pressure > 0f) return (byte)(Math.Clamp(pressure, 0f, 1f) * 255f);
        return digitalPressed ? (byte)0xFF : (byte)0x00;
    }

    private static void SetButton(byte[] report, int byteOffset, int bit, bool pressed)
    {
        if (pressed) report[byteOffset] |= (byte)(1 << bit);
    }

    private static void WriteShortBE(byte[] report, int offset, short value)
    {
        report[offset] = (byte)((ushort)value >> 8);
        report[offset + 1] = (byte)((ushort)value & 0xFF);
    }
}

/// <summary>
/// Target 5: Virtual Nintendo Switch Pro Controller — full 0x30 standard input report
/// with 12-bit calibrated sticks and 3 consecutive IMU samples (plan 07 §4.5).
/// </summary>
public sealed class SwitchProTarget : IVirtualControllerTarget
{
    public const ushort CenterX = 2048;
    public const ushort CenterY = 2048;
    public const int ReportLength = 78;

    public VirtualControllerTarget Target => VirtualControllerTarget.NintendoSwitchPro;
    public string DeviceName => "Virtual Nintendo Switch Pro Controller";

    public byte[] BuildInputReport(VirtualGamepadState state, ushort reportCounter = 0)
    {
        var b = state.Buttons;
        var report = new byte[ReportLength];
        report[0] = 0x30; // Standard input report

        // Timer (increments each report)
        report[1] = (byte)(reportCounter & 0xFF);
        report[2] = (byte)((reportCounter >> 8) & 0xFF);

        // Battery/conn info byte 2 (0x8X full charge, USB connected)
        report[2] |= 0x80;

        // Button groupings: byte 3 (right cluster), byte 4 (shared), byte 5 (left cluster)
        if (b.HasFlag(GamepadDigitalButtons.Y)) report[3] |= 1 << 0;
        if (b.HasFlag(GamepadDigitalButtons.X)) report[3] |= 1 << 1;
        if (b.HasFlag(GamepadDigitalButtons.B)) report[3] |= 1 << 2;
        if (b.HasFlag(GamepadDigitalButtons.A)) report[3] |= 1 << 3;
        if (b.HasFlag(GamepadDigitalButtons.RightBumper)) report[3] |= 1 << 5;  // R
        if (state.RightTrigger >= 0.95f) report[3] |= 1 << 6;                    // ZR
        if (b.HasFlag(GamepadDigitalButtons.Start)) report[3] |= 1 << 7;        // Plus

        if (b.HasFlag(GamepadDigitalButtons.LeftThumbClick)) report[4] |= 1 << 1;
        if (b.HasFlag(GamepadDigitalButtons.RightThumbClick)) report[4] |= 1 << 0;
        if (b.HasFlag(GamepadDigitalButtons.Capture)) report[4] |= 1 << 5;
        if (b.HasFlag(GamepadDigitalButtons.Guide)) report[4] |= 1 << 4;        // Home

        if (b.HasFlag(GamepadDigitalButtons.DpadDown)) report[5] |= 1 << 0;
        if (b.HasFlag(GamepadDigitalButtons.DpadUp)) report[5] |= 1 << 1;
        if (b.HasFlag(GamepadDigitalButtons.DpadRight)) report[5] |= 1 << 2;
        if (b.HasFlag(GamepadDigitalButtons.DpadLeft)) report[5] |= 1 << 3;
        if (b.HasFlag(GamepadDigitalButtons.LeftBumper)) report[5] |= 1 << 5;   // L
        if (state.LeftTrigger >= 0.95f) report[5] |= 1 << 6;                     // ZL
        if (b.HasFlag(GamepadDigitalButtons.Back)) report[5] |= 1 << 7;         // Minus

        // 12-bit analog sticks with calibrated center offsets
        WriteStick(report, 6, state.LeftStick);
        WriteStick(report, 9, state.RightStick);

        // 3 consecutive 6-axis IMU samples (5ms apart each) for ultra-low latency gyro aiming
        WriteImuSamples(report, 13, state.Motion);

        return report;
    }

    public static void WriteStick(byte[] report, int offset, StickPosition stick)
    {
        ushort x = (ushort)Math.Clamp(stick.X * 1462f + CenterX, 0f, 4095f);
        ushort y = (ushort)Math.Clamp(stick.Y * 1462f + CenterY, 0f, 4095f);
        report[offset] = (byte)(x & 0xFF);
        report[offset + 1] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        report[offset + 2] = (byte)(y >> 4);
    }

    public static void WriteImuSamples(byte[] report, int offset, MotionState motion)
    {
        for (int i = 0; i < 3; i++)
        {
            int baseOffset = offset + i * 12;
            WriteShortBE(report, baseOffset, (short)(motion.AccelX * 8192f));      // accel X (Gs -> raw)
            WriteShortBE(report, baseOffset + 2, (short)(motion.AccelY * 8192f));
            WriteShortBE(report, baseOffset + 4, (short)(motion.AccelZ * 8192f));
            WriteShortBE(report, baseOffset + 6, (short)(motion.GyroX * 16f));     // gyro X (deg/s -> raw)
            WriteShortBE(report, baseOffset + 8, (short)(motion.GyroY * 16f));
            WriteShortBE(report, baseOffset + 10, (short)(motion.GyroZ * 16f));
        }
    }

    private static void WriteShortBE(byte[] report, int offset, short value)
    {
        report[offset] = (byte)((ushort)value >> 8);
        report[offset + 1] = (byte)((ushort)value & 0xFF);
    }
}

/// <summary>
/// Factory producing the report formatter for each emulation target (plan 07 §6 deliverable).
/// </summary>
public static class VirtualTargetFactory
{
    public static IVirtualControllerTarget Create(VirtualControllerTarget target) => target switch
    {
        VirtualControllerTarget.Ds4 => new DualShock4Target(),
        VirtualControllerTarget.XboxOneBT => new XboxOneTarget(),
        VirtualControllerTarget.Ds3 => new DualShock3Target(),
        VirtualControllerTarget.NintendoSwitchPro => new SwitchProTarget(),
        VirtualControllerTarget.KeyboardAndMouse => throw new NotSupportedException(
            "Keyboard & Mouse output is synthesized via VirtualInputSimulator, not a HID gamepad report."),
        _ => new Xbox360Target()
    };
}
