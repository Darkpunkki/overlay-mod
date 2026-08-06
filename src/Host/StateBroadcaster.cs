using System.Collections.Concurrent;
using System.Threading.Channels;

namespace OverlayMod.Host;

/// <summary>
/// Fans the latest serialised state out to every connected overlay.
///
/// Each subscriber gets a one-slot channel that drops the older value when a new
/// one arrives: a client that stalls should resume at the current state, not
/// replay a backlog of stale frames. The latest payload is also retained so a
/// newly connected overlay renders immediately instead of waiting for the next tick.
/// </summary>
public sealed class StateBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public string? Latest { get; private set; }

    public int ClientCount => _clients.Count;

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var id = Guid.NewGuid();
        _clients[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_clients.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(string json)
    {
        Latest = json;
        foreach (var channel in _clients.Values) channel.Writer.TryWrite(json);
    }
}
