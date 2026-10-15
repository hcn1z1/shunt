using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Shunt.Engine;

public sealed class EngineEventBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null
    };

    public void Publish<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(json);
    }

    public async IAsyncEnumerable<string> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
                yield return message;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
