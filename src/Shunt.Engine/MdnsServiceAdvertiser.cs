using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shunt.Engine;

/// <summary>Minimal DNS-SD responder for the optional _rewasdengine._tcp.local service.</summary>
internal sealed class MdnsServiceAdvertiser : IAsyncDisposable
{
    private const string ServiceType = "_rewasdengine._tcp.local";
    private readonly UdpClient _client = new(AddressFamily.InterNetwork);
    private readonly IPEndPoint _multicast = new(IPAddress.Parse("224.0.0.251"), 5353);
    private readonly string _instance = $"Shunt ({Environment.MachineName})._rewasdengine._tcp.local";
    private readonly string _host = $"{Environment.MachineName}.local";
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public MdnsServiceAdvertiser(int port)
    {
        _port = port;
        using var routeProbe = new UdpClient(AddressFamily.InterNetwork);
        routeProbe.Connect("8.8.8.8", 53);
        _address = ((IPEndPoint)routeProbe.Client.LocalEndPoint!).Address;
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        _client.JoinMulticastGroup(_multicast.Address);
        _client.Ttl = 255;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var announceAt = DateTimeOffset.MinValue;
        Task<UdpReceiveResult> receive = _client.ReceiveAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= announceAt)
            {
                await _client.SendAsync(BuildAnswer(), _multicast, cancellationToken);
                announceAt = DateTimeOffset.UtcNow.AddSeconds(60);
            }

            var delay = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var completed = await Task.WhenAny(receive, delay);
            if (completed == receive)
            {
                var packet = await receive;
                if (IsServiceQuery(packet.Buffer))
                    await _client.SendAsync(BuildAnswer(), packet.RemoteEndPoint, cancellationToken);
                receive = _client.ReceiveAsync(cancellationToken).AsTask();
            }
        }
    }

    private static bool IsServiceQuery(byte[] packet)
    {
        if (packet.Length < 12) return false;
        int questionCount = (packet[4] << 8) | packet[5];
        int offset = 12;
        for (int question = 0; question < questionCount; question++)
        {
            var labels = new List<string>();
            int consumedOffset = -1;
            while (offset < packet.Length)
            {
                byte length = packet[offset++];
                if (length == 0) break;
                if ((length & 0xC0) == 0xC0)
                {
                    if (offset >= packet.Length) return false;
                    int pointer = ((length & 0x3F) << 8) | packet[offset++];
                    if (consumedOffset < 0) consumedOffset = offset;
                    offset = pointer;
                    continue;
                }
                if (length > 63 || offset + length > packet.Length) return false;
                labels.Add(Encoding.ASCII.GetString(packet, offset, length));
                offset += length;
            }
            if (consumedOffset >= 0) offset = consumedOffset;
            if (string.Equals(string.Join(".", labels), ServiceType, StringComparison.OrdinalIgnoreCase)) return true;
            if (offset + 4 > packet.Length) return false;
            offset += 4; // QTYPE and QCLASS
        }
        return false;
    }

    private byte[] BuildAnswer()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteU16(writer, 0); WriteU16(writer, 0x8400); // authoritative response
        WriteU16(writer, 0); WriteU16(writer, 4); WriteU16(writer, 0); WriteU16(writer, 0);
        WriteRecord(writer, ServiceType, 12, 1, 120, w => WriteName(w, _instance));
        WriteRecord(writer, _instance, 33, 0x8001, 120, w =>
        {
            WriteU16(w, 0); WriteU16(w, 0); WriteU16(w, (ushort)_port); WriteName(w, _host);
        });
        WriteRecord(writer, _instance, 16, 0x8001, 120, w =>
        {
            byte[] txt = Encoding.UTF8.GetBytes("product=Shunt");
            w.Write((byte)txt.Length); w.Write(txt);
        });
        WriteRecord(writer, _host, 1, 0x8001, 120, w => w.Write(_address.GetAddressBytes()));
        return stream.ToArray();
    }

    private static void WriteRecord(BinaryWriter writer, string name, ushort type, ushort @class, uint ttl, Action<BinaryWriter> writeData)
    {
        WriteName(writer, name); WriteU16(writer, type); WriteU16(writer, @class); WriteU32(writer, ttl);
        using var data = new MemoryStream();
        using (var dataWriter = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true)) writeData(dataWriter);
        WriteU16(writer, checked((ushort)data.Length)); writer.Write(data.ToArray());
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        foreach (string label in name.TrimEnd('.').Split('.'))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new InvalidOperationException("Invalid DNS label length.");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }
        writer.Write((byte)0);
    }

    private static void WriteU16(BinaryWriter writer, ushort value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    private static void WriteU32(BinaryWriter writer, uint value) { WriteU16(writer, (ushort)(value >> 16)); WriteU16(writer, (ushort)value); }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _client.Dispose();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        _stop.Dispose();
    }
}
