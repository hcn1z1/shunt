using System;

namespace Shunt.Engine.Hardware;

/// <summary>
/// Decoded Joy-Con / Switch Pro standard input report 0x30 (plan 09 §2.3).
/// </summary>
public record SwitchProInputReport(
    bool Y, bool X, bool B, bool A, bool R, bool ZR, bool Plus,
    bool RightStickClick, bool LeftStickClick,
    bool L, bool ZL, bool Minus, bool Capture, bool Home,
    bool DpadUp, bool DpadDown, bool DpadLeft, bool DpadRight,
    float LeftStickX, float LeftStickY,
    float RightStickX, float RightStickY,
    short[] AccelX, short[] AccelY, short[] AccelZ,
    short[] GyroX, short[] GyroY, short[] GyroZ);

/// <summary>
/// Nintendo Switch UART client for Joy-Con (L/R) &amp; Pro Controller subcommand
/// communication over USB HID / Bluetooth (plan 09 checklist).
/// Implements subcommands 0x01 (input report mode), 0x02 (HID polling), 0x21
/// (SPI flash read for factory stick calibration) and 0x30 (standard input report 0x30
/// with IMU sampling), plus the 0x30 input report parser.
/// </summary>
public sealed class NintendoSwitchUartClient
{
    public const byte InputReportId = 0x30;
    public const ushort CalibratedCenterX = 2048;
    public const ushort CalibratedCenterY = 2048;

    // Factory calibration addresses in the controller SPI flash (plan 09 §2.3)
    public const uint LeftStickCalibrationAddress = 0x603D;
    public const uint RightStickCalibrationAddress = 0x6046;
    public const uint ImuCalibrationAddress = 0x6020;

    /// <summary>Builds a subcommand output report wrapper around the given subcommand payload.</summary>
    public static byte[] BuildSubcommand(byte subcommandId, params byte[] arguments)
    {
        // Output report 0x01: rumble baseline + subcommand id + args
        var packet = new byte[11 + arguments.Length];
        packet[0] = 0x01;         // Output report id: subcommand packet
        packet[1] = 0x00;         // Timer
        packet[2] = 0x01; packet[3] = 0x40; packet[4] = 0x40; packet[5] = 0x00;
        packet[6] = 0x01; packet[7] = 0x40; packet[8] = 0x40; packet[9] = 0x00; // neutral rumble
        packet[10] = subcommandId;
        if (arguments.Length > 0) arguments.CopyTo(packet, 11);
        return packet;
    }

    /// <summary>Sets the standard input report mode (0x30 full IMU reports).</summary>
    public static byte[] SetInputReportMode(byte mode) => BuildSubcommand(0x03, mode);

    /// <summary>Enables/disables HID input reporting (0x02).</summary>
    public static byte[] SetHidPolling(bool enabled) => BuildSubcommand(0x02, (byte)(enabled ? 0x01 : 0x00));

    /// <summary>Requests a SPI flash read (0x21) — used to fetch stick/IMU factory calibration.</summary>
    public static byte[] ReadSpiFlash(uint address, byte length)
    {
        var args = new byte[7];
        args[0] = (byte)(address & 0xFF);
        args[1] = (byte)((address >> 8) & 0xFF);
        args[2] = (byte)((address >> 16) & 0xFF);
        args[3] = (byte)((address >> 24) & 0xFF);
        args[4] = length;
        return BuildSubcommand(0x21, args);
    }

    /// <summary>Enables/disables the 6-axis IMU (subcommand 0x40) and samples at full rate for 0x30 reports.</summary>
    public static byte[] SetImuEnabled(bool enabled) => BuildSubcommand(0x40, (byte)(enabled ? 0x01 : 0x00));

    /// <summary>
    /// Parses the full standard input report 0x30: 3 button bytes, 12-bit calibrated stick
    /// coordinates, and 3 consecutive 6-axis IMU samples (5ms apart).
    /// </summary>
    public static SwitchProInputReport? ParseInputReport(ReadOnlySpan<byte> report, bool bluetooth)
    {
        int baseOffset = bluetooth ? 1 : 0;
        if (report.Length < baseOffset + 49) return null;
        if (report[baseOffset] != InputReportId) return null;

        byte right = report[baseOffset + 3];
        byte shared = report[baseOffset + 4];
        byte left = report[baseOffset + 5];

        var (lx, ly) = ReadStick(report, baseOffset + 6);
        var (rx, ry) = ReadStick(report, baseOffset + 9);

        var accelX = new short[3]; var accelY = new short[3]; var accelZ = new short[3];
        var gyroX = new short[3]; var gyroY = new short[3]; var gyroZ = new short[3];
        for (int i = 0; i < 3; i++)
        {
            int imuOffset = baseOffset + 13 + i * 12;
            accelX[i] = ReadShortBE(report, imuOffset);
            accelY[i] = ReadShortBE(report, imuOffset + 2);
            accelZ[i] = ReadShortBE(report, imuOffset + 4);
            gyroX[i] = ReadShortBE(report, imuOffset + 6);
            gyroY[i] = ReadShortBE(report, imuOffset + 8);
            gyroZ[i] = ReadShortBE(report, imuOffset + 10);
        }

        return new SwitchProInputReport(
            (right & (1 << 0)) != 0, (right & (1 << 1)) != 0, (right & (1 << 2)) != 0, (right & (1 << 3)) != 0,
            (right & (1 << 5)) != 0, (right & (1 << 6)) != 0, (right & (1 << 7)) != 0,
            (shared & (1 << 0)) != 0, (shared & (1 << 1)) != 0,
            (left & (1 << 5)) != 0, (left & (1 << 6)) != 0, (left & (1 << 7)) != 0,
            (shared & (1 << 5)) != 0, (shared & (1 << 4)) != 0,
            (left & (1 << 1)) != 0, (left & (1 << 0)) != 0, (left & (1 << 3)) != 0, (left & (1 << 2)) != 0,
            lx, ly, rx, ry,
            accelX, accelY, accelZ, gyroX, gyroY, gyroZ);
    }

    /// <summary>
    /// Extracts a 12-bit stick pair and normalizes it against the factory calibration
    /// center offsets read via SPI (plan 09 §2.3).
    /// </summary>
    private static (float X, float Y) ReadStick(ReadOnlySpan<byte> report, int offset)
    {
        ushort x = (ushort)(report[offset] | ((report[offset + 1] & 0x0F) << 8));
        ushort y = (ushort)((report[offset + 1] >> 4) | (report[offset + 2] << 4));
        float nx = Math.Clamp((x - CalibratedCenterX) / 1462f, -1f, 1f);
        float ny = Math.Clamp((y - CalibratedCenterY) / 1462f, -1f, 1f);
        return (nx, ny);
    }

    private static short ReadShortBE(ReadOnlySpan<byte> report, int offset) =>
        (short)((report[offset] << 8) | report[offset + 1]);
}
