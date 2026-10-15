using System;

namespace Shunt.Engine.Hardware;

/// <summary>
/// DualSense Edge hardware controls (plan 09 §2.2).
/// </summary>
[Flags]
public enum DualSenseEdgeButtons : byte
{
    None = 0,
    ProfileLeftFn = 1 << 0,   // Left Fn beneath left thumbstick
    ProfileRightFn = 1 << 1,  // Right Fn beneath right thumbstick
    BackPaddleLeft = 1 << 2,  // LB1 half-dome / back lever
    BackPaddleRight = 1 << 3  // RB1 half-dome / back lever
}

/// <summary>
/// DualSense adaptive trigger bidirectional force-feedback modes (plan 09 §2.2).
/// </summary>
public enum DualSenseTriggerEffect
{
    Off = 0,
    Rigid = 1,
    Pulse = 2,
    Bow = 3,
    Galloping = 4,
    MachineGun = 5,
    SemiAuto = 6
}

/// <summary>
/// Decoded DualSense extended feature set from a raw USB/BT input report 0x31.
/// </summary>
public record DualSenseFeatureState(
    DualSenseEdgeButtons EdgeButtons,
    bool MicMuteLed,
    byte BatteryLevel,
    DualSenseTriggerEffect LeftTriggerEffect,
    DualSenseTriggerEffect RightTriggerEffect);

/// <summary>
/// DualSense &amp; DualSense Edge feature decoder: profile buttons (Fn L/R), back paddles
/// (LB1/RB1), mic mute LED, battery and adaptive-trigger telemetry from the standard
/// HID input report 0x31 (plan 09 §2.2).
/// </summary>
public static class DualSenseFeatureDecoder
{
    public const byte InputReportId = 0x31;
    /// <summary>Offset of the Edge hardware-control byte (0x24) in the 0x31 input report (USB layout).</summary>
    public const int EdgeButtonsOffset = 36;
    /// <summary>Offset of the battery/status byte in the 0x31 input report.</summary>
    public const int StatusOffset = 53;

    /// <summary>
    /// Decodes the extended feature state from a raw 0x31 input report.
    /// Accepts both USB (offsets as-is) and Bluetooth (1-byte report id prefix) layouts.
    /// </summary>
    public static DualSenseFeatureState Decode(ReadOnlySpan<byte> report, bool bluetooth)
    {
        int baseOffset = bluetooth ? 1 : 0;

        byte edgeByte = ReadByte(report, baseOffset + EdgeButtonsOffset);
        var edge = DualSenseEdgeButtons.None;
        if ((edgeByte & 0x04) != 0) edge |= DualSenseEdgeButtons.ProfileLeftFn;
        if ((edgeByte & 0x08) != 0) edge |= DualSenseEdgeButtons.ProfileRightFn;
        if ((edgeByte & 0x10) != 0) edge |= DualSenseEdgeButtons.BackPaddleLeft;
        if ((edgeByte & 0x20) != 0) edge |= DualSenseEdgeButtons.BackPaddleRight;

        byte status = ReadByte(report, baseOffset + StatusOffset);
        // Status nibble: low 4 bits encode battery level (0-10) when charging flag clear
        byte battery = (byte)((status & 0x0F) * 10);

        return new DualSenseFeatureState(
            edge,
            MicMuteLed: (edgeByte & 0x40) != 0,
            battery,
            DualSenseTriggerEffect.Off,
            DualSenseTriggerEffect.Off);
    }

    /// <summary>
    /// Builds the adaptive trigger output report command for the requested effect
    /// (bidirectional force feedback telemetry, plan 09 §2.2).
    /// </summary>
    public static byte[] BuildAdaptiveTriggerReport(bool rightTrigger, DualSenseTriggerEffect effect, byte strength)
    {
        // Output report 0x02 (USB): effect command block per trigger
        var report = new byte[48];
        report[0] = 0x02;
        report[rightTrigger ? 11 : 10] = 0x01; // enable effect mode flag
        int offset = rightTrigger ? 22 : 18;   // right trigger effect block offset
        report[offset] = (byte)effect;
        report[offset + 1] = strength;
        return report;
    }

    /// <summary>Friendly scancode names for the Edge hardware controls.</summary>
    public static string GetButtonName(DualSenseEdgeButtons button) => button switch
    {
        DualSenseEdgeButtons.ProfileLeftFn => "BTN_PROFILE_FN_LEFT",
        DualSenseEdgeButtons.ProfileRightFn => "BTN_PROFILE_FN_RIGHT",
        DualSenseEdgeButtons.BackPaddleLeft => "BTN_BACK_PEDAL_LEFT",
        DualSenseEdgeButtons.BackPaddleRight => "BTN_BACK_PEDAL_RIGHT",
        _ => "BTN_EDGE_NONE"
    };

    private static byte ReadByte(ReadOnlySpan<byte> report, int offset) =>
        offset >= 0 && offset < report.Length ? report[offset] : (byte)0;
}
