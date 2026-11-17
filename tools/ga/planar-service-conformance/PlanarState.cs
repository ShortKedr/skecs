using SKECS;
using SKECS.Numerics;
using SKECS.Serialization;

internal readonly record struct PlanarSample(int Slot, int Generation, Fixed X, Fixed Y, Fixed Vx, Fixed Vy)
{
    internal PlanarMotion Motion => new() { X = X, Y = Y, Vx = Vx, Vy = Vy };
}

internal readonly record struct PlanarPair(int First, int FirstGeneration, int Second, int SecondGeneration);
internal readonly record struct PlanarResult(PlanarSample[] Rows, PlanarPair[] Pairs, int Impulses);
internal readonly record struct PlanarBegin(PlanarPair Pair);

internal sealed class PlanarState
{
    internal const uint ExtensionId = 0x504c4e52;
    internal const uint ExtensionVersion = 1;
    internal const string Contract = "planar-unit-circles/q32.32-v1";
    private const int SlotLimit = 5;
    private readonly List<Owner> _slots = [];
    private readonly List<int> _free = [];
    private PlanarPair[] _history = [];

    internal long Tick { get; private set; }
    // Recomputed on every tick for the fixture's diagnostics. Continuation uses the saved contact history.
    internal int LastTickImpulseCount { get; private set; }
    internal IReadOnlyList<int> Free => _free;
    internal IReadOnlyList<PlanarPair> History => _history;

    internal readonly record struct Owner(int Entity, int EntityVersion, int Generation)
    {
        internal bool Alive => Entity >= 0;
    }

    internal Owner GetOwner(int slot) => _slots[slot];

    internal static PlanarState Seed(World world)
    {
        var state = new PlanarState();
        world.CreateEntity(); // World IDs deliberately differ from service slots.
        state.Spawn(world, -1, 0, 1, 1);
        state.Spawn(world, 0, 1, 0, 0);
        state.Spawn(world, 1, 0, -1, 1);
        state.Spawn(world, 20, 20, 0, 0);
        state.Spawn(world, 22, 20, 0, 0);
        return state;
    }

    internal void BeforeTick(World world, long tick)
    {
        Check(tick == Tick + 1, "Planar service ticks must be consecutive.");
        // The diagnostic lifecycle schedule is a fixed part of this fixture.
        // Tick in the checkpoint determines which requests remain.
        if (tick == 3)
        {
            Retire(world, 3);
            Retire(world, 4);
        }
        if (tick == 4)
        {
            Spawn(world, 20, 20, 0, 0);
            Spawn(world, 22, 20, 0, 0);
        }
    }

    private void Spawn(World world, int x, int y, int vx, int vy)
    {
        int slot = _free.Count == 0 ? _slots.Count : _free[^1];
        Check(slot < SlotLimit, "Planar body capacity exceeded.");
        int generation = slot == _slots.Count ? 1 : _slots[slot].Generation;
        var sample = new PlanarSample(slot, generation, Fixed.FromInt32(x), Fixed.FromInt32(y),
            Fixed.FromInt32(vx), Fixed.FromInt32(vy));
        var entity = world.CreateEntity(sample.Motion, new PlanarLink { Slot = slot, Generation = generation });
        var owner = new Owner(entity.Id, entity.Version, generation);
        if (slot == _slots.Count)
            _slots.Add(owner);
        else
        {
            _free.RemoveAt(_free.Count - 1);
            _slots[slot] = owner;
        }
    }

    private void Retire(World world, int slot)
    {
        var owner = _slots[slot];
        Check(owner.Alive, "Cannot retire a free body.");
        world.DestroyEntity(world.BindEntity(owner.Entity, owner.EntityVersion));
        _slots[slot] = new(-1, 0, checked(owner.Generation + 1));
        _free.Add(slot);
        _history = _history.Where(pair => pair.First != slot && pair.Second != slot).ToArray();
    }

    internal PlanarSample[] CaptureRows(World world)
    {
        var result = new List<PlanarSample>(_slots.Count - _free.Count);
        for (int slot = 0; slot < _slots.Count; slot++)
        {
            var owner = _slots[slot];
            if (!owner.Alive) continue;
            var motion = world.Get<PlanarMotion>(world.BindEntity(owner.Entity, owner.EntityVersion));
            result.Add(new(slot, owner.Generation, motion.X, motion.Y, motion.Vx, motion.Vy));
        }
        return result.ToArray();
    }

    internal void Apply(DeterministicOrderedApplyContext<PlanarFrame> context)
    {
        Check(context.TickIndex == Tick + 1, "Unexpected planar apply tick.");
        var observations = new PlanarSample?[_slots.Count];
        foreach (var item in context.Events)
        {
            var row = item.Get<PlanarObservation>();
            Check((uint)row.Slot < _slots.Count && observations[row.Slot] is null, "Invalid or repeated planar observation.");
            var owner = _slots[row.Slot];
            Check(owner.Alive && owner.Generation == row.Generation &&
                owner.Entity == item.Key.SourceId && owner.EntityVersion == item.Key.SourceGeneration, "Stale planar body observation.");
            observations[row.Slot] = new(row.Slot, row.Generation, row.X, row.Y, row.Vx, row.Vy);
        }
        var samples = new List<PlanarSample>(_slots.Count - _free.Count);
        for (int slot = 0; slot < observations.Length; slot++)
        {
            Check(_slots[slot].Alive == observations[slot].HasValue, "Missing planar body observation.");
            if (observations[slot] is { } sample) samples.Add(sample);
        }
        var result = PlanarContacts.Solve(samples.ToArray());
        foreach (var row in result.Rows)
        {
            var owner = _slots[row.Slot];
            Check(context.TryWrite(owner.Entity, owner.EntityVersion, row.Motion), "Planar body disappeared during apply.");
        }
        foreach (var pair in result.Pairs)
            if (!_history.Contains(pair)) context.Commands.Emit(new PlanarBegin(pair));
        _history = result.Pairs;
        LastTickImpulseCount = result.Impulses;
        Tick = context.TickIndex;
    }

    internal CheckpointExtension Capture(bool includeHistory = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Tick);
        writer.Write(_slots.Count);
        foreach (var owner in _slots)
        {
            writer.Write(owner.Entity);
            writer.Write(owner.EntityVersion);
            writer.Write(owner.Generation);
        }
        writer.Write(_free.Count);
        foreach (int slot in _free) writer.Write(slot);
        writer.Write(includeHistory ? _history.Length : 0);
        if (includeHistory)
        {
            foreach (var pair in _history)
            {
                writer.Write(pair.First);
                writer.Write(pair.FirstGeneration);
                writer.Write(pair.Second);
                writer.Write(pair.SecondGeneration);
            }
        }
        return new(ExtensionId, ExtensionVersion, true, stream.ToArray());
    }

    internal static PlanarState Restore(World world, CheckpointExtension extension)
    {
        Check(extension.Id == ExtensionId && extension.Version == ExtensionVersion && extension.Required, "Unexpected planar checkpoint schema.");
        using var stream = new MemoryStream(extension.Data, writable: false);
        using var reader = new BinaryReader(stream);
        var state = new PlanarState { Tick = reader.ReadInt64() };
        Check(state.Tick >= 0, "Invalid planar checkpoint tick.");
        int count = ReadCount(SlotLimit);
        for (int slot = 0; slot < count; slot++)
            state._slots.Add(new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        int free = ReadCount(count);
        for (int index = 0; index < free; index++)
            state._free.Add(reader.ReadInt32());
        state._history = new PlanarPair[ReadCount(count * (count - 1) / 2)];
        for (int index = 0; index < state._history.Length; index++)
            state._history[index] = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        Check(stream.Position == stream.Length, "Unexpected planar checkpoint suffix.");
        state.Validate(world);
        return state;

        int ReadCount(int maximum)
        {
            int value = reader.ReadInt32();
            Check(value >= 0 && value <= maximum, "Invalid planar checkpoint count.");
            return value;
        }
    }

    private void Validate(World world)
    {
        var checkpoint = world.OpenCheckpoint();
        var owners = new HashSet<int>();
        var free = _free.ToHashSet();
        Check(free.Count == _free.Count && free.All(slot => (uint)slot < _slots.Count), "Invalid planar free list.");
        for (int slot = 0; slot < _slots.Count; slot++)
        {
            var owner = _slots[slot];
            Check(owner.Generation > 0 && free.Contains(slot) != owner.Alive, "Planar slot and free list disagree.");
            if (!owner.Alive)
            {
                Check(owner.Entity == -1 && owner.EntityVersion == 0, "Free planar slot has an owner.");
                continue;
            }
            Check((uint)owner.Entity < checkpoint.SlotCount && owners.Add(owner.Entity), "Invalid or repeated planar owner.");
            var entry = checkpoint.GetSlot(owner.Entity);
            Check(entry.Alive && entry.Version == owner.EntityVersion, "Stale planar owner.");
            var entity = world.BindEntity(owner.Entity, owner.EntityVersion);
            Check(checkpoint.TryGet(entity, out PlanarLink link) && link.Slot == slot && link.Generation == owner.Generation &&
                checkpoint.TryGet(entity, out PlanarMotion _), "Invalid PlanarLink.");
        }
        for (int id = 0; id < checkpoint.SlotCount; id++)
        {
            var entry = checkpoint.GetSlot(id);
            if (entry.Alive && checkpoint.TryGet(world.BindEntity(id, entry.Version), out PlanarLink _))
                Check(owners.Contains(id), "Unmapped PlanarLink.");
        }
        (int First, int Second) last = (-1, -1);
        foreach (var pair in _history)
        {
            Check((uint)pair.First < _slots.Count && (uint)pair.Second < _slots.Count && pair.First < pair.Second &&
                (pair.First, pair.Second).CompareTo(last) > 0, "Planar pair order is not canonical.");
            var first = _slots[pair.First];
            var second = _slots[pair.Second];
            Check(first.Alive && second.Alive && first.Generation == pair.FirstGeneration &&
                second.Generation == pair.SecondGeneration, "Stale planar pair in checkpoint.");
            last = (pair.First, pair.Second);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
