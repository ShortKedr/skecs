namespace SKECS.GA;

/// <summary>Binds an immutable Int32 snapshot captured before a tick to its runner tick index.</summary>
public sealed class GaTickSnapshot
{
    private Binding? _binding;

    public void Bind(long tickIndex, GaSnapshotPacket packet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);
        ArgumentNullException.ThrowIfNull(packet);
        packet.RequireInt32Layout();
        if (_binding is { } previous && tickIndex <= previous.Tick)
            throw new InvalidOperationException("A bound snapshot cannot be replaced for the same or an earlier tick.");
        _binding = new Binding(tickIndex, packet);
    }

    internal GaSnapshotPacket Get(long tickIndex) =>
        _binding is { } binding && binding.Tick == tickIndex ? binding.Packet :
        throw new InvalidOperationException($"The GA snapshot must be bound before tick {tickIndex} begins.");

    internal sealed class Reader(GaTickSnapshot source)
    {
        private Binding? _active;

        internal GaSnapshotPacket Get(long tickIndex)
        {
            if (_active is { } active && active.Tick == tickIndex)
                return active.Packet;
            var packet = source.Get(tickIndex);
            _active = new Binding(tickIndex, packet);
            return packet;
        }
    }

    private sealed record Binding(long Tick, GaSnapshotPacket Packet);
}
