namespace SKECS.GA;

/// <summary>Publishes immutable flat buffers at logical tick boundaries on the runner owner thread.</summary>
public sealed class GaTickBuffer
{
    private readonly SortedDictionary<long, GaBufferPacket> _pending = new();
    private long _tick = -1;
    private GaBufferPacket? _active;

    /// <summary>Binds a packet to a future tick. Binding later ticks does not replace an active packet.</summary>
    public void Bind(long tickIndex, GaBufferPacket packet)
    {
        if (tickIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(tickIndex), tickIndex, "Value must be non-negative.");
        if (packet is null)
            throw new ArgumentNullException(nameof(packet));
        if (tickIndex <= _tick || !_pending.TryAdd(tickIndex, packet))
            throw new InvalidOperationException("A read buffer cannot replace a packet for a bound or active tick.");
    }

    private GaBufferPacket Get(long tickIndex)
    {
        if (tickIndex < _tick)
            throw new InvalidOperationException("A read buffer cannot be reused by an earlier tick.");
        if (tickIndex > _tick)
        {
            _pending.TryGetValue(tickIndex, out _active);
            while (_pending.Count != 0 && _pending.Keys.First() <= tickIndex)
                _pending.Remove(_pending.Keys.First());
            _tick = tickIndex;
        }
        return _active ?? throw new InvalidOperationException($"No GA read buffer is bound for tick {tickIndex}.");
    }

    internal sealed class Reader(GaTickBuffer source, string schema, int rowWords)
    {
        internal GaBufferPacket Get(long tickIndex)
        {
            var packet = source.Get(tickIndex);
            packet.RequireSchema(schema, rowWords);
            return packet;
        }
    }
}
