using System;
using System.Buffers.Binary;
using Shunt.Core.Models;

namespace Shunt.Emulation;

/// <summary>Writes the six-axis sensor portion of ViGEm's 63-byte DS4 extended report.</summary>
public static class DualShock4ReportFormatter
{
    public const int MinimumMotionReportLength = 24;
    public const int GyroOffset = 12;
    public const int AccelOffset = 18;
    public const float GyroUnitsPerDegreePerSecond = 16f;
    public const float AccelUnitsPerG = 8192f;

    public static void WriteMotion(Span<byte> report, MotionState motion)
    {
        if (report.Length < MinimumMotionReportLength)
            throw new ArgumentException($"A DS4 extended report must contain at least {MinimumMotionReportLength} bytes to write motion data.", nameof(report));

        WriteScaled(report, GyroOffset, motion.GyroX, GyroUnitsPerDegreePerSecond);
        WriteScaled(report, GyroOffset + 2, motion.GyroY, GyroUnitsPerDegreePerSecond);
        WriteScaled(report, GyroOffset + 4, motion.GyroZ, GyroUnitsPerDegreePerSecond);
        WriteScaled(report, AccelOffset, motion.AccelX, AccelUnitsPerG);
        WriteScaled(report, AccelOffset + 2, motion.AccelY, AccelUnitsPerG);
        WriteScaled(report, AccelOffset + 4, motion.AccelZ, AccelUnitsPerG);
    }

    private static void WriteScaled(Span<byte> report, int offset, float value, float scale)
    {
        if (!float.IsFinite(value)) value = 0f;
        var encoded = (short)Math.Clamp(MathF.Round(value * scale), short.MinValue, short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(offset, sizeof(short)), encoded);
    }
}
