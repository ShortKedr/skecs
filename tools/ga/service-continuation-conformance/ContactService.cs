using SKECS;
using SKECS.Numerics;
using SKECS.Serialization;

internal readonly record struct BodySlot(int Generation, int Owner, int OwnerGeneration)
{
    public bool Alive => Owner >= 0;
}

internal readonly record struct ContactPair(int First, int FirstGeneration, int Second, int SecondGeneration);
internal readonly record struct ContactStarted(ContactPair Pair);
internal readonly record struct Lifecycle(long Tick, int RetireSlot, int SpawnPosition);

// The fixture uses equal-mass, unit-radius circles constrained to one axis.
internal sealed class ContactService
{
    internal const uint ExtensionId = 0x53474338;
    internal const uint ExtensionVersion = 1;
    internal const string Contract = "line-circles/q32.32-v1";
    private const int MaximumBodies = 16;
    private static readonly Fixed ContactDistance = Fixed.FromInt32(2);
    private readonly List<BodySlot> _slots = new();
    private readonly List<int> _free = new();
    private readonly List<ContactPair> _previous = new();
    private readonly Queue<Lifecycle> _scheduled = new();
    private uint _random = 0x8d4c2a31u;

    internal long CompletedTick { get; private set; }
    internal IReadOnlyList<BodySlot> Slots => _slots;
    internal IReadOnlyList<int> Free => _free;
    internal IReadOnlyList<ContactPair> Previous => _previous;
    internal int PendingLifecycle => _scheduled.Count;
    internal uint RandomState => _random;
    internal int Impulses { get; private set; }

    internal static ContactService Seed(World world)
    {
        var service = new ContactService();
        world.CreateEntity(); // Force world entity IDs and service slot IDs to differ.
        var half = Fixed.FromRaw(Fixed.One.Raw / 2);
        service.Create(world, -1, half);
        service.Create(world, 0, Fixed.Zero);
        service.Create(world, 1, -half);
        service.Create(world, 20, Fixed.Zero);
        service.Create(world, 40, Fixed.Zero);
        service._scheduled.Enqueue(new(4, 3, 0));
        service._scheduled.Enqueue(new(4, 4, 0));
        service._scheduled.Enqueue(new(5, -1, 20));
        service._scheduled.Enqueue(new(5, -1, 40));
        service._scheduled.Enqueue(new(8, 2, 0));
        service._scheduled.Enqueue(new(9, -1, 1));
        return service;
    }

    internal void BeforeTick(World world, long tick)
    {
        Check(tick == CompletedTick + 1, "Service ticks must be consecutive.");
        while (_scheduled.TryPeek(out var request) && request.Tick == tick)
        {
            _scheduled.Dequeue();
            if (request.RetireSlot >= 0)
                Retire(world, request.RetireSlot);
            else
                Create(world, request.SpawnPosition, Fixed.FromRaw((long)(NextRandom() & 15) << 24));
        }
    }

    private uint NextRandom()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return _random;
    }

    private void Create(World world, int position, Fixed velocity)
    {
        int slot = _free.Count == 0 ? _slots.Count : _free[^1];
        Check(slot < MaximumBodies, "Exceeded the fixture body limit.");
        int generation = slot == _slots.Count ? 1 : _slots[slot].Generation;
        var entity = world.CreateEntity(new ContactMotion { Position = Fixed.FromInt32(position), Velocity = velocity },
            new PhysicsLink { Slot = slot, Generation = generation });
        var body = new BodySlot(generation, entity.Id, entity.Version);
        if (slot == _slots.Count)
            _slots.Add(body);
        else
        {
            _free.RemoveAt(_free.Count - 1);
            _slots[slot] = body;
        }
    }

    private void Retire(World world, int slot)
    {
        var old = _slots[slot];
        Check(old.Alive, "Cannot retire a free body slot.");
        world.DestroyEntity(world.BindEntity(old.Owner, old.OwnerGeneration));
        _slots[slot] = new BodySlot(checked(old.Generation + 1), -1, 0);
        _free.Add(slot);
        _previous.RemoveAll(pair => pair.First == slot || pair.Second == slot);
    }

    internal void Apply(DeterministicOrderedApplyContext<IntegrationFrame> context)
    {
        Check(context.TickIndex == CompletedTick + 1, "Unexpected service tick.");
        var rows = new BodyObservation?[_slots.Count];
        foreach (var item in context.Events)
        {
            var sample = item.Get<BodyObservation>();
            Check((uint)sample.Slot < _slots.Count && rows[sample.Slot] is null, "Invalid or duplicate body sample.");
            var body = _slots[sample.Slot];
            Check(body.Alive && body.Generation == sample.Generation && body.Owner == item.Key.SourceId &&
                body.OwnerGeneration == item.Key.SourceGeneration, "Stale body sample.");
            rows[sample.Slot] = sample;
        }

        var current = new List<ContactPair>();
        var velocity = new Fixed[rows.Length];
        Impulses = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            Check(_slots[i].Alive == rows[i].HasValue, "Missing live body sample.");
            if (rows[i] is { } row)
                velocity[i] = row.Velocity;
        }

        for (int first = 0; first < rows.Length; first++)
        {
            if (rows[first] is not { } left)
                continue;
            for (int second = first + 1; second < rows.Length; second++)
            {
                if (rows[second] is not { } right)
                    continue;
                var difference = right.Position - left.Position;
                if (difference < -ContactDistance || difference > ContactDistance)
                    continue;
                var pair = new ContactPair(first, left.Generation, second, right.Generation);
                current.Add(pair);
                if (!_previous.Contains(pair))
                    context.Commands.Emit(new ContactStarted(pair));

                bool approaching = difference >= Fixed.Zero
                    ? velocity[first] > velocity[second]
                    : velocity[first] < velocity[second];
                if (approaching)
                {
                    (velocity[first], velocity[second]) = (velocity[second], velocity[first]);
                    Impulses++;
                }
            }
        }

        for (int slot = 0; slot < rows.Length; slot++)
        {
            if (rows[slot] is not { } integrated)
                continue;
            var body = _slots[slot];
            var motion = new ContactMotion { Position = integrated.Position, Velocity = velocity[slot] };
            Check(context.TryWrite(body.Owner, body.OwnerGeneration, motion), "Body owner disappeared in ordered service.");
        }

        _previous.Clear();
        _previous.AddRange(current);
        CompletedTick = context.TickIndex;
    }

    internal CheckpointExtension Capture(bool includeHistory = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(CompletedTick);
        writer.Write(_random);
        writer.Write(_slots.Count);
        foreach (var body in _slots)
        {
            writer.Write(body.Generation);
            writer.Write(body.Owner);
            writer.Write(body.OwnerGeneration);
        }
        writer.Write(_free.Count);
        foreach (int slot in _free)
            writer.Write(slot);

        writer.Write(includeHistory ? _previous.Count : 0);
        if (includeHistory)
        {
            foreach (var pair in _previous)
            {
                writer.Write(pair.First);
                writer.Write(pair.FirstGeneration);
                writer.Write(pair.Second);
                writer.Write(pair.SecondGeneration);
            }
        }

        writer.Write(_scheduled.Count);
        foreach (var next in _scheduled)
        {
            writer.Write(next.Tick);
            writer.Write(next.RetireSlot);
            writer.Write(next.SpawnPosition);
        }
        return new CheckpointExtension(ExtensionId, ExtensionVersion, true, stream.ToArray());
    }

    internal static ContactService Restore(World world, CheckpointExtension extension)
    {
        Check(extension.Id == ExtensionId && extension.Version == ExtensionVersion && extension.Required,
            "Unexpected contact service extension.");
        using var stream = new MemoryStream(extension.Data, writable: false);
        using var reader = new BinaryReader(stream);
        var service = new ContactService { CompletedTick = reader.ReadInt64(), _random = reader.ReadUInt32() };
        Check(service.CompletedTick >= 0 && service._random != 0, "Invalid service tick or RNG.");
        int count = ReadCount(MaximumBodies);
        for (int index = 0; index < count; index++)
            service._slots.Add(new BodySlot(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        int freeCount = ReadCount(count);
        for (int index = 0; index < freeCount; index++)
            service._free.Add(reader.ReadInt32());
        int pairCount = ReadCount(count * (count - 1) / 2);
        for (int index = 0; index < pairCount; index++)
            service._previous.Add(new ContactPair(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        int scheduled = ReadCount(16);
        for (int index = 0; index < scheduled; index++)
            service._scheduled.Enqueue(new Lifecycle(reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32()));
        Check(stream.Position == stream.Length, "Unexpected trailing service checkpoint data.");
        service.Validate(world);
        return service;

        int ReadCount(int maximum)
        {
            int value = reader.ReadInt32();
            Check(value >= 0 && value <= maximum, "Service checkpoint count out of bounds.");
            return value;
        }
    }

    internal void Validate(World world)
    {
        var checkpoint = world.OpenCheckpoint();
        var ownerSet = new HashSet<int>();
        var free = _free.ToHashSet();
        Check(free.Count == _free.Count && free.All(slot => (uint)slot < _slots.Count), "Invalid service free list.");
        for (int slot = 0; slot < _slots.Count; slot++)
        {
            var body = _slots[slot];
            Check(body.Generation > 0 && free.Contains(slot) != body.Alive, "Service free list disagrees with slots.");
            if (!body.Alive)
            {
                Check(body.Owner == -1 && body.OwnerGeneration == 0, "Free body carries an owner.");
                continue;
            }
            Check((uint)body.Owner < checkpoint.SlotCount && ownerSet.Add(body.Owner), "Invalid or duplicate body owner.");
            var owner = checkpoint.GetSlot(body.Owner);
            Check(owner.Alive && owner.Version == body.OwnerGeneration, "Stale entity in body-slot map.");
            var entity = world.BindEntity(body.Owner, body.OwnerGeneration);
            Check(checkpoint.TryGet(entity, out PhysicsLink link) && link.Slot == slot &&
                link.Generation == body.Generation && checkpoint.TryGet(entity, out ContactMotion _), "Invalid PhysicsLink.");
        }

        for (int id = 0; id < checkpoint.SlotCount; id++)
        {
            var entity = checkpoint.GetSlot(id);
            if (entity.Alive && checkpoint.TryGet(world.BindEntity(id, entity.Version), out PhysicsLink _))
                Check(ownerSet.Contains(id), "World has an unmapped PhysicsLink.");
        }

        int first = -1;
        int second = -1;
        foreach (var pair in _previous)
        {
            Check(pair.First < pair.Second && (uint)pair.First < _slots.Count && (uint)pair.Second < _slots.Count &&
                (pair.First > first || pair.First == first && pair.Second > second), "History is not canonical.");
            var left = _slots[pair.First];
            var right = _slots[pair.Second];
            Check(left.Alive && right.Alive && left.Generation == pair.FirstGeneration &&
                right.Generation == pair.SecondGeneration, "History references a stale body.");
            (first, second) = (pair.First, pair.Second);
        }

        long last = CompletedTick;
        foreach (var next in _scheduled)
        {
            Check(next.Tick > CompletedTick && next.Tick >= last && next.RetireSlot >= -1 &&
                next.RetireSlot < _slots.Count, "Invalid pending lifecycle request.");
            last = next.Tick;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}
