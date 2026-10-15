using System;
using System.Collections.Generic;

namespace Shunt.Engine.Hardware;

/// <summary>
/// Xbox Elite Series 1 &amp; 2 rear paddle inputs (plan 09 §2.1).
/// </summary>
[Flags]
public enum ElitePaddleMask : byte
{
    None = 0,
    P1 = 1 << 0, // Upper left
    P2 = 1 << 1, // Upper right
    P3 = 1 << 2, // Lower left
    P4 = 1 << 3  // Lower right
}

/// <summary>
/// Extracts the Elite paddle inputs as independent raw inputs. In standard Windows XInput the
/// firmware clamps paddles to mirror face buttons; reWASD "unclamps" them by issuing vendor
/// USB/BT feature reports (<c>0x07</c> / <c>0x0B</c>) and reading the raw HID collections
/// (plan 09 §2.1).
/// </summary>
public static class XboxElitePaddleDecoder
{
    /// <summary>Vendor feature report that releases the paddle clamp over USB.</summary>
    public const byte UsbUnclampReportId = 0x07;
    /// <summary>Vendor feature report that releases the paddle clamp over Bluetooth.</summary>
    public const byte BluetoothUnclampReportId = 0x0B;

    /// <summary>
    /// Builds the vendor unclamp feature report payload so paddles report as
    /// BTN_LEFT_UPPER_PADDLE etc. instead of mirroring face buttons.
    /// </summary>
    public static byte[] BuildUnclampFeatureReport(bool bluetooth)
    {
        // [report id, enable separate paddle reporting, 0x00 padding]
        return [(byte)(bluetooth ? BluetoothUnclampReportId : UsbUnclampReportId), 0x01, 0x00];
    }

    /// <summary>
    /// Decodes the Elite vendor HID input collection into a paddle mask.
    /// Paddle bits live in the vendor-defined trailing byte of the extended report.
    /// </summary>
    /// <param name="vendorReport">Raw vendor report bytes (at least 1 byte of paddle state).</param>
    /// <param name="paddleStateOffset">Offset of the paddle state byte inside the vendor report.</param>
    public static ElitePaddleMask Decode(ReadOnlySpan<byte> vendorReport, int paddleStateOffset = 0)
    {
        if (vendorReport.IsEmpty || paddleStateOffset < 0 || paddleStateOffset >= vendorReport.Length)
            return ElitePaddleMask.None;

        byte state = vendorReport[paddleStateOffset];
        var mask = ElitePaddleMask.None;
        if ((state & 0x01) != 0) mask |= ElitePaddleMask.P1;
        if ((state & 0x02) != 0) mask |= ElitePaddleMask.P2;
        if ((state & 0x04) != 0) mask |= ElitePaddleMask.P3;
        if ((state & 0x08) != 0) mask |= ElitePaddleMask.P4;
        return mask;
    }

    /// <summary>Friendly scancode names for each paddle (BTN_* identifiers).</summary>
    public static string GetButtonName(ElitePaddleMask paddle) => paddle switch
    {
        ElitePaddleMask.P1 => "BTN_LEFT_UPPER_PADDLE",
        ElitePaddleMask.P2 => "BTN_RIGHT_UPPER_PADDLE",
        ElitePaddleMask.P3 => "BTN_LEFT_LOWER_PADDLE",
        ElitePaddleMask.P4 => "BTN_RIGHT_LOWER_PADDLE",
        _ => "BTN_PADDLE_NONE"
    };

    /// <summary>Enumerates the individual paddles active in the mask.</summary>
    public static IEnumerable<ElitePaddleMask> Split(ElitePaddleMask mask)
    {
        foreach (ElitePaddleMask paddle in new[] { ElitePaddleMask.P1, ElitePaddleMask.P2, ElitePaddleMask.P3, ElitePaddleMask.P4 })
        {
            if (mask.HasFlag(paddle)) yield return paddle;
        }
    }
}
