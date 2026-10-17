using System;

namespace Shunt.Engine.Hardware;

/// <summary>A decoded IMU sample. Acceleration is expressed in g and angular velocity in degrees/second.</summary>
public readonly record struct ImuSensorSample(
    long TimestampMilliseconds,
    ushort Sequence,
    float AccelX,
    float AccelY,
    float AccelZ,
    float GyroX,
    float GyroY,
    float GyroZ);

/// <summary>Offsets and scale factors for a vendor HID input report.</summary>
public sealed record ImuReportFormat(
    byte ReportId,
    int MinimumLength,
    int SequenceOffset,
    int AccelXOffset,
    int AccelYOffset,
    int AccelZOffset,
    int GyroXOffset,
    int GyroYOffset,
    int GyroZOffset,
    float AccelerationGPerLsb,
    float GyroDegreesPerSecondPerLsb)
{
    public static ImuReportFormat GameSirG7Pro { get; } = new(
        ReportId: 0x12,
        MinimumLength: 16,
        SequenceOffset: 1,
        AccelXOffset: 4,
        AccelYOffset: 6,
        AccelZOffset: 8,
        GyroXOffset: 10,
        GyroYOffset: 12,
        GyroZOffset: 14,
        AccelerationGPerLsb: 1f / 8192f,
        GyroDegreesPerSecondPerLsb: 1f / 16f);

    public bool TryParse(ReadOnlySpan<byte> report, long timestampMilliseconds, out ImuSensorSample sample)
    {
        sample = default;
        if (report.Length < MinimumLength) return false;
        if (report[0] != ReportId && report[0] != 0x10 && report[0] != 0x12)
            return false;

        static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
            System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, sizeof(short)));

        sample = new ImuSensorSample(
            timestampMilliseconds,
            report[SequenceOffset],
            ReadInt16(report, AccelXOffset) * AccelerationGPerLsb,
            ReadInt16(report, AccelYOffset) * AccelerationGPerLsb,
            ReadInt16(report, AccelZOffset) * AccelerationGPerLsb,
            ReadInt16(report, GyroXOffset) * GyroDegreesPerSecondPerLsb,
            ReadInt16(report, GyroYOffset) * GyroDegreesPerSecondPerLsb,
            ReadInt16(report, GyroZOffset) * GyroDegreesPerSecondPerLsb);
        return true;
    }
}

/// <summary>Supported hardware identity and its currently documented report layout.</summary>
public sealed record ImuDeviceProfile(ushort VendorId, ushort ProductId, string Name, ImuReportFormat ReportFormat)
{
    public static IReadOnlyList<ImuDeviceProfile> KnownProfiles { get; } = new[]
    {
        new ImuDeviceProfile(0x3537, 0x1004, "GameSir G7 Pro / 8K", ImuReportFormat.GameSirG7Pro),
        new ImuDeviceProfile(0x3537, 0x100A, "GameSir G7 Pro", ImuReportFormat.GameSirG7Pro),
        new ImuDeviceProfile(0x3537, 0x1010, "GameSir G7 Pro", ImuReportFormat.GameSirG7Pro)
    };

    public static ImuDeviceProfile? Find(int vendorId, int productId)
    {
        foreach (var profile in KnownProfiles)
            if (profile.VendorId == vendorId && profile.ProductId == productId) return profile;
        return null;
    }
}
