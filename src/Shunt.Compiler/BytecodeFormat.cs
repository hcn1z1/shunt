using System;
using System.IO;
using System.Text;
using Shunt.Core.Models;
using Shunt.Profiles.Models;

namespace Shunt.Compiler;

/// <summary>
/// Packed binary representation of compiled reWASD profile instructions.
/// </summary>
public static class BytecodeConstants
{
    public const uint Magic = 0x44535752; // 'RWSD'
    public const uint Version = 0x00030000; // Schema 3.0
    public const uint TargetXbox360 = 1;
    public const uint TargetXboxOne = 2;
    public const uint TargetDualShock4 = 3;
}

public record CompiledBytecodeHeader(
    uint Magic,
    uint Version,
    uint TargetControllerType,
    uint ShiftsCount,
    uint ShiftTableOffset,
    uint MaskTableOffset,
    uint MacroBlockOffset);

public static class BytecodeWriter
{
    public static byte[] CompileToBytes(RewasdProfileDocument doc)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        uint targetType = (doc.Devices.Virtual?.Gamepads?.Find(g => g.Id == 1)?.Type?.ToLowerInvariant()) switch
        {
            "ds4" or "dualshock" or "dualshock4" => BytecodeConstants.TargetDualShock4,
            "xboxone" => BytecodeConstants.TargetXboxOne,
            _ => BytecodeConstants.TargetXbox360
        };

        var shifts = doc.Shifts ?? [];
        uint shiftsCount = (uint)Math.Max(1, shifts.Count);

        // Header placeholder (28 bytes)
        writer.Write(BytecodeConstants.Magic);
        writer.Write(BytecodeConstants.Version);
        writer.Write(targetType);
        writer.Write(shiftsCount);
        writer.Write((uint)0); // ShiftTableOffset placeholder
        writer.Write((uint)0); // MaskTableOffset placeholder
        writer.Write((uint)0); // MacroBlockOffset placeholder

        // 1. Shift Layer Table
        uint shiftTableOffset = (uint)ms.Position;
        for (int i = 0; i < shiftsCount; i++)
        {
            ushort shiftId = (ushort)(i < shifts.Count ? shifts[i].Id : (i + 1));
            writer.Write(shiftId);
            writer.Write(0.08f); // Left inner deadzone
            writer.Write(0.02f); // Left outer deadzone
            writer.Write(0.08f); // Right inner deadzone
            writer.Write(0.02f); // Right outer deadzone

            // 64-entry button lookup table
            for (int b = 0; b < 64; b++)
            {
                // Default identity or mapped target
                uint targetBtn = (uint)(b < 32 ? (1 << b) : 0);
                writer.Write(targetBtn);
            }
        }

        // 2. Mask Definitions Table
        uint maskTableOffset = (uint)ms.Position;
        var masks = doc.Masks ?? [];
        writer.Write((uint)masks.Count);
        foreach (var mask in masks)
        {
            writer.Write((uint)mask.Id);
            writer.Write((uint)mask.Set.Count);
            foreach (var trig in mask.Set)
            {
                writer.Write((uint)trig.DeviceId);
                writer.Write((uint)trig.ButtonId);
            }
        }

        // 3. Macro Instruction Block
        uint macroBlockOffset = (uint)ms.Position;
        var mappings = doc.Mappings ?? [];
        int macroCount = 0;
        foreach (var m in mappings)
        {
            if (m.Macros != null) macroCount += m.Macros.Count;
        }
        writer.Write((uint)macroCount);

        foreach (var m in mappings)
        {
            if (m.Macros == null) continue;
            foreach (var step in m.Macros)
            {
                if (step.Gamepad != null)
                {
                    writer.Write((byte)1); // KeyDown
                    writer.Write((ushort)step.Gamepad.ButtonId);
                    writer.Write((int)0);
                }
                else if (step.DelayMs.HasValue)
                {
                    writer.Write((byte)3); // Delay
                    writer.Write((ushort)0);
                    writer.Write(step.DelayMs.Value);
                }
                else
                {
                    writer.Write((byte)0); // None
                    writer.Write((ushort)0);
                    writer.Write((int)0);
                }
            }
        }

        // Rewind and patch table offsets
        ms.Seek(16, SeekOrigin.Begin);
        writer.Write(shiftTableOffset);
        writer.Write(maskTableOffset);
        writer.Write(macroBlockOffset);

        writer.Flush();
        return ms.ToArray();
    }

    public static CompiledBytecodeHeader ReadHeader(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        uint magic = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint targetType = reader.ReadUInt32();
        uint shiftsCount = reader.ReadUInt32();
        uint shiftTableOffset = reader.ReadUInt32();
        uint maskTableOffset = reader.ReadUInt32();
        uint macroBlockOffset = reader.ReadUInt32();

        return new CompiledBytecodeHeader(
            magic,
            version,
            targetType,
            shiftsCount,
            shiftTableOffset,
            maskTableOffset,
            macroBlockOffset);
    }
}
