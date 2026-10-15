using System.Buffers.Binary;

namespace Shunt.Engine.Hardware;

public enum DongleCommand : byte { SetState = 0x01, UploadMap = 0x02, Ping = 0x03 }
public sealed record DongleFrame(DongleCommand Command, byte[] Payload);

/// <summary>Framing, CRC and Heatshrink-compatible decoding for the ESP32 serial link.</summary>
public static class DongleProtocol
{
    public const byte StartMarker = 0xAA;
    public const int HeaderLength = 4;
    public const int TrailerLength = 2;
    public const int MaximumPayloadLength = 65535;

    public static byte[] Encode(DongleCommand command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumPayloadLength) throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] frame = new byte[HeaderLength + payload.Length + TrailerLength];
        frame[0] = StartMarker;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1, 2), checked((ushort)payload.Length));
        frame[3] = (byte)command;
        payload.CopyTo(frame.AsSpan(HeaderLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(frame.Length - TrailerLength), ComputeCrc16(frame.AsSpan(1, frame.Length - 3)));
        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, out DongleFrame? decoded)
    {
        decoded = null;
        if (frame.Length < HeaderLength + TrailerLength || frame[0] != StartMarker) return false;
        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(1, 2));
        if (frame.Length != HeaderLength + payloadLength + TrailerLength) return false;
        ushort expected = BinaryPrimitives.ReadUInt16LittleEndian(frame[^TrailerLength..]);
        if (expected != ComputeCrc16(frame.Slice(1, frame.Length - 3))) return false;
        decoded = new DongleFrame((DongleCommand)frame[3], frame.Slice(HeaderLength, payloadLength).ToArray());
        return true;
    }

    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    /// <summary>Decodes a Heatshrink bitstream (window=8, lookahead=4 by default).</summary>
    public static byte[] DecompressHeatshrink(ReadOnlySpan<byte> input, int windowBits = 8, int lookaheadBits = 4, int maxOutputBytes = 16 * 1024 * 1024)
    {
        if (windowBits is < 4 or > 15) throw new ArgumentOutOfRangeException(nameof(windowBits));
        if (lookaheadBits is < 3 or > 15 || lookaheadBits >= windowBits) throw new ArgumentOutOfRangeException(nameof(lookaheadBits));
        if (maxOutputBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));

        var output = new List<byte>(Math.Min(input.Length * 2, maxOutputBytes));
        var bits = new BitReader(input);
        while (bits.TryRead(1, out uint tag))
        {
            if (tag == 1)
            {
                if (!bits.TryRead(8, out uint literal)) break;
                AddOutput((byte)literal);
            }
            else
            {
                if (!bits.TryRead(windowBits, out uint encodedDistance) || !bits.TryRead(lookaheadBits, out uint encodedLength)) break;
                int distance = checked((int)encodedDistance + 1);
                int count = checked((int)encodedLength + 1);
                if (distance > output.Count) throw new InvalidDataException("Heatshrink back-reference points before the output buffer.");
                if (output.Count + count > maxOutputBytes) throw new InvalidDataException("Heatshrink output exceeds the configured limit.");
                for (int i = 0; i < count; i++) output.Add(output[output.Count - distance]);
            }
        }
        return [.. output];

        void AddOutput(byte value)
        {
            if (output.Count >= maxOutputBytes) throw new InvalidDataException("Heatshrink output exceeds the configured limit.");
            output.Add(value);
        }
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _input;
        private int _bitOffset;
        public BitReader(ReadOnlySpan<byte> input) { _input = input; _bitOffset = 0; }
        public bool TryRead(int count, out uint value)
        {
            value = 0;
            if (_bitOffset + count > _input.Length * 8) return false;
            for (int i = 0; i < count; i++)
            {
                int index = _bitOffset++;
                value = (value << 1) | (uint)((_input[index >> 3] >> (7 - (index & 7))) & 1);
            }
            return true;
        }
    }
}
