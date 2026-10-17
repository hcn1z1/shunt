using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Shunt.Core.Models;

namespace Shunt.Engine;

/// <summary>Minimal Cemuhook DSU 1001 server for controller status and continuous motion data.</summary>
public sealed class DsuMotionServer : IAsyncDisposable
{
    public const int DefaultPort = 26760;
    private const uint VersionRequest = 0x00100000;
    private const uint ControllerInfoRequest = 0x00100001;
    private const uint DataRequest = 0x00100002;
    private const int ProtocolVersion = 1001;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, DsuClient> _clients = new();
    private readonly object _stateLock = new();
    private GamepadState _state = new();
    private uint _packetNumber;
    private Task? _receiveTask;
    private Task? _sendTask;

    private sealed record DsuClient(IPEndPoint Endpoint, DateTimeOffset LastRequest);

    public bool IsRunning => _receiveTask is { IsCompleted: false } && _sendTask is { IsCompleted: false };
    public int Port { get; }
    public string? LastError { get; private set; }

    public DsuMotionServer(int port = DefaultPort)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
    }

    public void Start()
    {
        if (IsRunning) return;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_lifetime.Token));
        _sendTask = Task.Run(() => SendLoopAsync(_lifetime.Token));
    }

    public void UpdateState(GamepadState state)
    {
        lock (_stateLock) _state = state;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!TryReadMessageType(received.Buffer, out var messageType)) continue;
                switch (messageType)
                {
                    case VersionRequest:
                        await SendAsync(BuildPacket(messageType, new byte[] { 0xE9, 0x03 }), received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                        break;
                    case ControllerInfoRequest:
                        await ReplyControllerInfoAsync(received.Buffer, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataRequest:
                        _clients[received.RemoteEndPoint.ToString()] = new DsuClient(received.RemoteEndPoint, DateTimeOffset.UtcNow);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { LastError = ex.Message; }
    }

    private async Task ReplyControllerInfoAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        if (request.Length < 25) return;
        var count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(20, 4)), 0, 4);
        for (var i = 0; i < count && 24 + i < request.Length; i++)
        {
            var slot = request[24 + i];
            if (slot > 3) continue;
            var payload = new byte[12];
            payload[0] = slot;
            payload[1] = 2; // Connected
            payload[2] = 1; // Full gyro
            payload[3] = 1; // USB
            payload[10] = 0x05; // Full battery
            await SendAsync(BuildPacket(ControllerInfoRequest, payload), endpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var pair in _clients.ToArray())
                {
                    if (now - pair.Value.LastRequest > TimeSpan.FromSeconds(5))
                    {
                        _clients.TryRemove(pair.Key, out _);
                        continue;
                    }

                    GamepadState state;
                    lock (_stateLock) state = _state;
                    var packet = BuildPacket(DataRequest, BuildControllerData(state, _packetNumber++));
                    await SendAsync(packet, pair.Value.Endpoint, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { LastError = ex.Message; }
    }

    private static byte[] BuildControllerData(GamepadState state, uint packetNumber)
    {
        var payload = new byte[80];
        payload[0] = 0; // Slot
        payload[1] = 2; // Connected
        payload[2] = 1; // Full gyro
        payload[3] = 1; // USB
        payload[10] = 0x05; // Full battery
        payload[11] = 1; // Connected
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), packetNumber);
        if (state.HasButton(GamepadButtons.DpadLeft)) payload[16] |= 1 << 7;
        if (state.HasButton(GamepadButtons.DpadDown)) payload[16] |= 1 << 6;
        if (state.HasButton(GamepadButtons.DpadRight)) payload[16] |= 1 << 5;
        if (state.HasButton(GamepadButtons.DpadUp)) payload[16] |= 1 << 4;
        if (state.HasButton(GamepadButtons.Start)) payload[16] |= 1 << 3;
        if (state.HasButton(GamepadButtons.RightThumb)) payload[16] |= 1 << 2;
        if (state.HasButton(GamepadButtons.LeftThumb)) payload[16] |= 1 << 1;
        if (state.HasButton(GamepadButtons.Back)) payload[16] |= 1;
        if (state.HasButton(GamepadButtons.Y)) payload[17] |= 1 << 7;
        if (state.HasButton(GamepadButtons.B)) payload[17] |= 1 << 6;
        if (state.HasButton(GamepadButtons.A)) payload[17] |= 1 << 5;
        if (state.HasButton(GamepadButtons.X)) payload[17] |= 1 << 4;
        if (state.HasButton(GamepadButtons.RightShoulder)) payload[17] |= 1 << 3;
        if (state.HasButton(GamepadButtons.LeftShoulder)) payload[17] |= 1 << 2;
        if (state.RightTrigger > 0) payload[17] |= 1 << 1;
        if (state.LeftTrigger > 0) payload[17] |= 1;
        payload[18] = state.HasButton(GamepadButtons.Guide) ? (byte)1 : (byte)0;
        payload[19] = state.HasButton(GamepadButtons.TouchpadClick) ? (byte)1 : (byte)0;
        payload[20] = state.LeftStick.ToByteX();
        payload[21] = state.LeftStick.ToByteY();
        payload[22] = state.RightStick.ToByteX();
        payload[23] = state.RightStick.ToByteY();
        payload[24] = state.HasButton(GamepadButtons.DpadLeft) ? (byte)255 : (byte)0;
        payload[25] = state.HasButton(GamepadButtons.DpadDown) ? (byte)255 : (byte)0;
        payload[26] = state.HasButton(GamepadButtons.DpadRight) ? (byte)255 : (byte)0;
        payload[27] = state.HasButton(GamepadButtons.DpadUp) ? (byte)255 : (byte)0;
        payload[28] = AxisPressure(state.HasButton(GamepadButtons.Y) ? 1f : 0f);
        payload[29] = AxisPressure(state.HasButton(GamepadButtons.B) ? 1f : 0f);
        payload[30] = AxisPressure(state.HasButton(GamepadButtons.A) ? 1f : 0f);
        payload[31] = AxisPressure(state.HasButton(GamepadButtons.X) ? 1f : 0f);
        payload[32] = AxisPressure(state.HasButton(GamepadButtons.RightShoulder) ? 1f : 0f);
        payload[33] = AxisPressure(state.HasButton(GamepadButtons.LeftShoulder) ? 1f : 0f);
        payload[34] = AxisPressure(state.RightTrigger);
        payload[35] = AxisPressure(state.LeftTrigger);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(48, 8), (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000));
        WriteSingle(payload, 56, state.AccelX);
        WriteSingle(payload, 60, state.AccelY);
        WriteSingle(payload, 64, state.AccelZ);
        WriteSingle(payload, 68, state.GyroVelX);
        WriteSingle(payload, 72, state.GyroVelY);
        WriteSingle(payload, 76, state.GyroVelZ);
        return payload;
    }

    private static byte AxisPressure(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);
    private static void WriteSingle(Span<byte> bytes, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(offset, 4), float.IsFinite(value) ? value : 0f);

    private static bool TryReadMessageType(byte[] packet, out uint messageType)
    {
        messageType = 0;
        if (packet.Length < 20 || packet[0] != (byte)'D' || packet[1] != (byte)'S' || packet[2] != (byte)'U' || packet[3] != (byte)'C') return false;
        var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6, 2));
        if (BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)) != ProtocolVersion || length > packet.Length - 16 || length < 4) return false;
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4));
        if (expectedCrc != ComputeCrc(packet)) return false;
        messageType = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16, 4));
        return true;
    }

    private static byte[] BuildPacket(uint messageType, byte[] payload)
    {
        var packet = new byte[20 + payload.Length];
        packet[0] = (byte)'D'; packet[1] = (byte)'S'; packet[2] = (byte)'U'; packet[3] = (byte)'S';
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), (ushort)(packet.Length - 16));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), 0x5348554E); // SHUN
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), messageType);
        payload.CopyTo(packet, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), ComputeCrc(packet));
        return packet;
    }

    private async Task SendAsync(byte[] packet, IPEndPoint endpoint, CancellationToken cancellationToken) =>
        _ = await _udp.SendAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);

    private static uint ComputeCrc(ReadOnlySpan<byte> packet)
    {
        uint crc = 0xFFFFFFFF;
        for (var i = 0; i < packet.Length; i++)
        {
            var value = i is >= 8 and < 12 ? (byte)0 : packet[i];
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _udp.Dispose();
        if (_receiveTask is not null) try { await _receiveTask.ConfigureAwait(false); } catch { }
        if (_sendTask is not null) try { await _sendTask.ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
    }
}
