using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SKECS;

namespace AdaptivePlacementFixture;

[StructLayout(LayoutKind.Sequential, Size = 128)]
internal record struct Body(int Accumulated);
internal record struct Effect(int Value);
internal record struct Marker(int Value);
internal record struct ExternalConsumer(int Value);

internal sealed class Simulation(int size, int seed, bool quiet)
{
    private readonly Entity[] _entities = new Entity[size];
    private readonly int[] _events = new int[size];
    private uint _input = (uint)seed;
    private long _eventFold;
    private AdaptiveQuery<Body, Effect> _effects;
    private AdaptiveQuery<Marker, Effect> _markers;
    private Query<Body, ExternalConsumer> _dense;
    private AdaptiveQueryDescription _effectMembership;
    private AdaptiveQueryDescription _markerMembership;

    internal static readonly ComponentPlacementOptions Permission = new ComponentPlacementOptions()
        .AllowAdaptiveStorage<Effect>().AllowAdaptiveStorage<Marker>()
        .AllowAdaptiveStorage<ExternalConsumer>().RequireArchetypeStorage<ExternalConsumer>();

    internal void Initialize(World world)
    {
        var factory = world.PrepareEntity<Body, ExternalConsumer>();
        for (var i = 0; i < _entities.Length; i++)
        {
            var entity = factory.Create(new Body(i), new ExternalConsumer(i ^ 17));
            _entities[i] = entity;
            if (i % 29 == 0) world.Add(entity, new Effect(i));
            if (i % 59 == 0) world.Add(entity, new Marker(i));
        }
        _effects = world.QueryAdaptive<Body, Effect>();
        _markers = world.QueryAdaptive<Marker, Effect>();
        _dense = world.Query<Body, ExternalConsumer>();
        if (!quiet)
        {
            _effectMembership = world.AdaptiveQueryBuilder().With<Effect>().Build();
            _markerMembership = world.AdaptiveQueryBuilder().With<Marker>().Build();
        }
    }

    internal long Step(World world, int tick, TransitionEvidence? evidence = null)
    {
        Array.Clear(_events);
        if (!quiet)
        {
            for (var operation = 0; operation < _entities.Length / 8; operation++)
            {
                ref var entity = ref _entities[Next(_entities.Length)];
                switch (Next(16))
                {
                    case 0:
                        world.DestroyEntity(entity);
                        entity = world.CreateEntity(new Body(tick), new ExternalConsumer(tick ^ 17));
                        break;
                    case 1:
                        Toggle<Marker>(world, entity, new Marker(tick), evidence);
                        break;
                    default:
                        Toggle<Effect>(world, entity, new Effect(tick), evidence);
                        break;
                }
            }
        }

        if (evidence != null)
        {
            evidence.Sample(typeof(Effect), _effectMembership.EntityCount);
            evidence.Sample(typeof(Marker), _markerMembership.EntityCount);
        }

        var queryStart = Stopwatch.GetTimestamp();
        var update = new ApplyEffect(_events);
        _effects.ForEach(ref update);
        var bonus = new ApplyMarker(_events);
        _markers.ForEach(ref bonus);
        var queryTicks = Stopwatch.GetTimestamp() - queryStart;
        foreach (var item in _events) _eventFold = unchecked(_eventFold * 31 + item);
        return queryTicks;
    }

    internal void Dense()
    {
        var update = new DenseUpdate();
        _dense.ForEach(ref update);
    }

    internal string CanonicalHash(World world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var state = world.OpenCheckpoint();
        Span<int> row = stackalloc int[12];
        row[0] = state.SlotCount;
        row[1] = state.FreeCount;
        row[2] = unchecked((int)_eventFold);
        row[3] = unchecked((int)(_eventFold >> 32));
        row[4] = unchecked((int)_input);
        hash.AppendData(MemoryMarshal.AsBytes(row[..5]));
        var free = new int[state.FreeCount];
        state.CopyFreeIds(free);
        hash.AppendData(MemoryMarshal.AsBytes(free.AsSpan()));
        for (var id = 0; id < state.SlotCount; id++)
        {
            var slot = state.GetSlot(id);
            row.Clear();
            row[0] = id;
            row[1] = slot.Version;
            row[2] = slot.Alive ? 1 : 0;
            if (slot.Alive)
            {
                var entity = world.BindEntity(id, slot.Version);
                row[3] = state.GetComponentCount(entity);
                if (state.TryGet<Body>(entity, out var body)) { row[4] = 1; row[5] = body.Accumulated; }
                if (state.TryGet<Effect>(entity, out var effect)) { row[6] = 1; row[7] = effect.Value; }
                if (state.TryGet<Marker>(entity, out var marker)) { row[8] = 1; row[9] = marker.Value; }
                if (state.TryGet<ExternalConsumer>(entity, out var consumer)) { row[10] = 1; row[11] = consumer.Value; }
            }
            hash.AppendData(MemoryMarshal.AsBytes(row));
        }
        hash.AppendData(MemoryMarshal.AsBytes(_events.AsSpan()));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private int Next(int max)
    {
        _input ^= _input << 13;
        _input ^= _input >> 17;
        _input ^= _input << 5;
        return (int)(_input % (uint)max);
    }

    private static void Toggle<T>(World world, Entity entity, T value, TransitionEvidence? evidence) where T : unmanaged
    {
        if (!world.Remove<T>(entity)) world.Add(entity, value);
        // Body (128 bytes) and ExternalConsumer (4) exist before and after every transition.
        evidence?.Record(typeof(T), 132);
    }

    private readonly struct ApplyEffect(int[] events) : IQueryProcessor<Body, Effect>
    {
        public void Execute(Entity entity, ref Body body, ref Effect effect)
        {
            body.Accumulated = unchecked(body.Accumulated + effect.Value);
            events[entity.Id] = body.Accumulated;
        }
    }

    private readonly struct ApplyMarker(int[] events) : IQueryProcessor<Marker, Effect>
    {
        public void Execute(Entity entity, ref Marker marker, ref Effect effect)
        {
            effect.Value ^= marker.Value;
            events[entity.Id] ^= effect.Value;
        }
    }

    private readonly struct DenseUpdate : IQueryProcessor<Body, ExternalConsumer>
    {
        public void Execute(Entity entity, ref Body body, ref ExternalConsumer consumer) =>
            body.Accumulated = unchecked(body.Accumulated + consumer.Value);
    }
}

internal sealed record Signal(string Name, long Transitions, long AvoidableNeighborBytes, long EstimatedIndexBytes,
    double MeanDensity, double PeakDensity);

internal sealed class TransitionEvidence
{
    private readonly Dictionary<Type, (long Count, long Bytes)> _observed = new();
    private readonly Dictionary<Type, (int Frames, long Rows, int Peak)> _occupancy = new();

    internal void Record(Type type, int bytes)
    {
        var previous = _observed.GetValueOrDefault(type);
        _observed[type] = (previous.Count + 1, previous.Bytes + bytes);
    }

    internal void Sample(Type type, int rows)
    {
        var previous = _occupancy.GetValueOrDefault(type);
        _occupancy[type] = (previous.Frames + 1, previous.Rows + rows, Math.Max(previous.Peak, rows));
    }

    // The model filters candidates. Runtime measurements determine CPU and memory outcomes.
    internal (Type Type, Signal Signal)[] Candidates(int entities) => _observed
        .Where(entry => Simulation.Permission.CanUsePackedPool(entry.Key))
        .Select(entry =>
        {
            var occupancy = _occupancy[entry.Key];
            return (Type: entry.Key, Signal: new Signal(entry.Key.FullName!, entry.Value.Count, entry.Value.Bytes,
                (4L + 2L * IntPtr.Size) * entities, (double)occupancy.Rows / ((long)occupancy.Frames * entities),
                (double)occupancy.Peak / entities));
        })
        .Where(entry => entry.Signal.AvoidableNeighborBytes > entry.Signal.EstimatedIndexBytes)
        .OrderByDescending(entry => entry.Signal.AvoidableNeighborBytes)
        .ThenBy(entry => entry.Signal.Name, StringComparer.Ordinal).ToArray();
}
