namespace SKECS.GA;

/// <summary>Binds immutable snapshots to the capture boundaries of one runner.</summary>
public sealed class GaTickSnapshot
{
    private static readonly GaSnapshotLayout Int32Layout = new([GaScalar.Int32]);
    private Binding? _external;
    private GaSnapshotPacket? _activeExternal;
    private long _lastExternalTick = -1;
    private long _currentTick = -1;
    private Binding? _prepared;

    /// <summary>Supplies a snapshot captured before a tick begins.</summary>
    public void Bind(long tickIndex, GaSnapshotPacket packet)
    {
        if (tickIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(tickIndex), tickIndex, "Value must be non-negative.");
        if (packet is null)
            throw new ArgumentNullException(nameof(packet));
        if (tickIndex <= _lastExternalTick || tickIndex <= _currentTick)
            throw new InvalidOperationException("A bound snapshot cannot be replaced for the same or an earlier tick.");
        _external = new Binding(new Position(tickIndex, default, 0, 0), packet);
        _lastExternalTick = tickIndex;
    }

    /// <summary>Captures declared component copies for subsequent producers in the current tick.</summary>
    /// <remarks>Rows missing the component are omitted. The codec writes one Int32 word.</remarks>
    public DeterministicSnapshotPreparation<TInput> PrepareInt32<TInput, T>(GaSnapshotPack<T> pack)
        where TInput : unmanaged where T : unmanaged => Prepare<TInput, T>(Int32Layout, pack);

    /// <summary>Captures an immutable packet using the declared layout and component codec.</summary>
    /// <remarks>Rows missing the component are omitted.</remarks>
    public DeterministicSnapshotPreparation<TInput> Prepare<TInput, T>(GaSnapshotLayout layout, GaSnapshotPack<T> pack)
        where TInput : unmanaged where T : unmanaged
    {
        if (layout is null)
            throw new ArgumentNullException(nameof(layout));
        if (pack is null)
            throw new ArgumentNullException(nameof(pack));
        return source =>
        {
            var position = new Position(source.TickIndex, source.Stage, source.SystemOrder, source.KernelOrder);
            if (position.Tick < _currentTick || _prepared is { } previous && previous.Position.CompareTo(position) >= 0)
                throw new InvalidOperationException("GA snapshot preparations must advance in runner order.");

            var keys = new GaSnapshotKey[source.Count];
            int stride = layout.ValueWordCount;
            var words = new uint[checked(source.Count * stride)];
            int count = 0;
            for (int row = 0; row < source.Count; row++)
            {
                if (!source.TryRead<T>(row, out var component))
                    continue;
                var entity = source.GetEntity(row);
                keys[count] = new GaSnapshotKey(entity.Id, entity.Version);
                pack(in component, words.AsSpan(count * stride, stride));
                count++;
            }
            var packet = GaSnapshotPacket.FromRows(layout, keys.AsSpan(0, count), words.AsSpan(0, count * stride));
            AdvanceTick(position.Tick);
            _prepared = new Binding(position, packet);
        };
    }

    private GaSnapshotPacket Read(Position position)
    {
        AdvanceTick(position.Tick);
        if (_prepared is { } prepared && prepared.Position.CompareTo(position) < 0)
            return prepared.Packet;
        if (_activeExternal is { } external)
            return external;
        throw new InvalidOperationException($"The GA snapshot has no capture before tick {position.Tick}, " +
            $"stage {position.Stage}, system {position.System}, kernel {position.Kernel}.");
    }

    private void AdvanceTick(long tick)
    {
        if (tick < _currentTick)
            throw new InvalidOperationException("GA snapshots cannot be used by an earlier tick.");
        if (tick == _currentTick)
            return;
        _activeExternal = null;
        if (_external is { } external && external.Position.Tick <= tick)
        {
            if (external.Position.Tick == tick)
                _activeExternal = external.Packet;
            _external = null;
        }
        _prepared = null;
        _currentTick = tick;
    }

    internal sealed class Reader(GaTickSnapshot source, string? snapshotSchema)
    {
        private Binding? _active;

        internal GaSnapshotPacket Get(long tickIndex, SystemStage stage, int systemOrder, int kernelOrder)
        {
            var position = new Position(tickIndex, stage, systemOrder, kernelOrder);
            if (_active is { } active && active.Position == position)
                return active.Packet;
            var packet = source.Read(position);
            packet.RequireSchema(snapshotSchema);
            _active = new Binding(position, packet);
            return packet;
        }
    }

    private sealed record Binding(Position Position, GaSnapshotPacket Packet);

    private readonly record struct Position(long Tick, SystemStage Stage, int System, int Kernel)
    {
        internal int CompareTo(Position other) =>
            (Tick, Stage, System, Kernel).CompareTo((other.Tick, other.Stage, other.System, other.Kernel));
    }
}
