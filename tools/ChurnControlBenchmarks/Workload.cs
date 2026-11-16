using System.Globalization;
using System.Runtime.InteropServices;
using SKECS;

internal sealed class Workload
{
    private readonly string _scenario;
    private readonly Entity[] _a;
    private readonly Entity[] _b;
    private readonly int[] _randomOrder;
    private EntityFactory<Position, PayloadA> _factoryA;
    private EntityFactory<Position, PayloadB> _factoryB;
    private EntityFactory<Position, NarrowPayload> _narrowFactory;
    private EntityFactory<Position, WidePayload> _wideFactory;
    private Query<Position> _query;
    private ComponentAccessor<Position> _accessor;
    private long _getSum;

    public Workload(string scenario, int entities, int seed)
    {
        _scenario = scenario;
        _a = new Entity[entities];
        _b = new Entity[scenario is "separated" or "simultaneous" or "sizes" ? entities : 0];
        _randomOrder = Enumerable.Range(0, entities).ToArray();
        new Random(seed).Shuffle(_randomOrder.AsSpan());
    }

    public static bool Supports(string scenario) => scenario is
        "for-each" or "chunks" or "get" or "set" or "same" or "separated" or "simultaneous" or "sizes" or "quiet" or "transitions";

    public void Initialize(World world)
    {
        _factoryA = world.PrepareEntity<Position, PayloadA>();
        _query = world.Query<Position>();
        _accessor = world.GetAccessor<Position>();
        if (_scenario is "separated" or "simultaneous")
            _factoryB = world.PrepareEntity<Position, PayloadB>();
        if (_scenario == "sizes")
        {
            _narrowFactory = world.PrepareEntity<Position, NarrowPayload>();
            _wideFactory = world.PrepareEntity<Position, WidePayload>();
        }
        if (_scenario is "for-each" or "chunks" or "get" or "set" or "quiet" or "transitions")
            Fill(_factoryA, _a, new PayloadA(11), 1);
    }

    public unsafe string Step(World world, int frame)
    {
        switch (_scenario)
        {
            case "for-each":
                AdvanceRows();
                return "for-each";
            case "chunks":
                foreach (var chunk in _query.Chunks())
                    for (var i = 0; i < chunk.Count; i++) chunk.Component1[i].Value++;
                return "chunks";
            case "get":
                long sum = 0;
                foreach (var index in _randomOrder) sum = unchecked(sum + world.Get<Position>(_a[index]).Value);
                _getSum = sum;
                return "get";
            case "set":
                foreach (var index in _randomOrder) world.Set(_a[index], new Position(((long)frame << 32) + index));
                return "set";
            case "quiet":
                QuietFrame(world, frame);
                return "quiet";
            case "transitions":
                if (frame % 2 == 0)
                {
                    foreach (var entity in _a) world.Add(entity, new NarrowPayload(-frame));
                    AdvanceRows();
                    return "add-component";
                }
                foreach (var entity in _a) world.Remove<NarrowPayload>(entity);
                AdvanceRows();
                return "remove-component";
            default:
                var phase = StructuralFrame(world, frame);
                AdvanceRows();
                return phase;
        }
    }

    private string StructuralFrame(World world, int frame)
    {
        var startValue = (long)frame << 32;
        if (_scenario == "sizes")
        {
            switch (frame % 6)
            {
                case 0: Fill(_wideFactory, _b, new WidePayload(19 + frame), startValue); return "fill-wide";
                case 1: Destroy(world, _b); return "drain-wide";
                case 2: Fill(_factoryA, _a, new PayloadA(11 + frame), startValue); return "fill-a";
                case 3: Destroy(world, _a); return "drain-a";
                case 4: Fill(_narrowFactory, _b, new NarrowPayload(17 + frame), startValue); return "fill-narrow";
                default: Destroy(world, _b); return "drain-narrow";
            }
        }

        var phase = frame % (_scenario == "same" ? 2 : 4);
        if (phase == 0)
        {
            Fill(_factoryA, _a, new PayloadA(11 + frame), startValue);
            return "fill-a";
        }
        if ((phase == 1 && _scenario != "simultaneous") || (phase == 2 && _scenario == "simultaneous"))
        {
            Destroy(world, _a);
            return "drain-a";
        }
        if (phase == 3)
        {
            Destroy(world, _b);
            return "drain-b";
        }

        Fill(_factoryB, _b, new PayloadB(13 + frame), startValue);
        return "fill-b";
    }

    private void QuietFrame(World world, int frame)
    {
        var replacements = Math.Max(1, _a.Length / 512);
        var offset = ((long)frame * replacements) % _a.Length;
        for (var i = 0; i < replacements; i++)
        {
            var index = _randomOrder[(int)((offset + i) % _a.Length)];
            world.DestroyEntity(_a[index]);
            _a[index] = _factoryA.Create(new Position(index), new PayloadA(frame));
        }
        AdvanceRows();
    }

    private void AdvanceRows()
    {
        var advance = new Advance();
        _query.ForEach(ref advance);
    }

    private static void Fill<T>(EntityFactory<Position, T> factory, Entity[] output, T payload, long start)
        where T : unmanaged
    {
        for (var i = 0; i < output.Length; i++) output[i] = factory.Create(new Position(start + i), payload);
    }

    private static void Destroy(World world, Entity[] handles)
    {
        foreach (var entity in handles) world.DestroyEntity(entity);
    }

    public int OccupiedChunks()
    {
        var occupied = 0;
        foreach (var chunk in _query.Chunks())
            if (chunk.Count != 0) occupied++;
        return occupied;
    }

    public unsafe string Checksum(World world)
    {
        ulong hash = Mix(14695981039346656037UL, _getSum);
        ulong membership = 0;
        var aliveCount = 0;
        HashHandles(world, _a, ref hash, ref membership, ref aliveCount);
        HashHandles(world, _b, ref hash, ref membership, ref aliveCount);

        // Membership is independent of the order in which the physical chunks are traversed.
        ulong queryMembership = 0;
        var queryCount = 0;
        foreach (var chunk in _query.Chunks())
        {
            queryCount += chunk.Count;
            for (var row = 0; row < chunk.Count; row++)
                queryMembership = unchecked(queryMembership + Member(chunk.Entities[row], chunk.Versions[row], chunk.Component1[row].Value));
        }
        if (queryCount != aliveCount || queryMembership != membership)
            throw new InvalidOperationException("The query and entity handles disagree on frame membership or component values.");
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private void HashHandles(World world, Entity[] handles, ref ulong hash, ref ulong membership, ref int aliveCount)
    {
        foreach (var entity in handles)
        {
            hash = Mix(Mix(hash, entity.Id), entity.Version);
            if (!world.IsAlive(entity))
            {
                hash = Mix(hash, 0);
                if (_accessor.Has(entity)) throw new InvalidOperationException("The accessor still sees a dead entity.");
                continue;
            }

            aliveCount++;
            var position = world.Get<Position>(entity).Value;
            if (_accessor.Get(entity).Value != position)
                throw new InvalidOperationException("The accessor and world disagree on the entity component value.");
            membership = unchecked(membership + Member(entity.Id, entity.Version, position));
            hash = Mix(Mix(hash, 1), position);
            if (world.TryGet<PayloadA>(entity, out var a)) hash = Mix(Mix(hash, 2), a.Marker);
            if (world.TryGet<PayloadB>(entity, out var b)) hash = Mix(Mix(hash, 3), b.Marker);
            if (world.TryGet<NarrowPayload>(entity, out var narrow)) hash = Mix(Mix(hash, 4), narrow.Marker);
            if (world.TryGet<WidePayload>(entity, out var wide)) hash = Mix(Mix(hash, 5), wide.Marker);
        }
    }

    private static ulong Member(int id, int version, long value) => Mix(Mix(Mix(0, id), version), value);

    private static ulong Mix(ulong hash, long value) => unchecked((hash ^ (ulong)value) * 1099511628211UL);

    private struct Position(long value) { public long Value = value; }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly struct PayloadA(long marker) { public readonly long Marker = marker; }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly struct PayloadB(long marker) { public readonly long Marker = marker; }

    [StructLayout(LayoutKind.Sequential, Size = 12)]
    private readonly struct NarrowPayload(long marker) { public readonly long Marker = marker; }

    [StructLayout(LayoutKind.Sequential, Size = 96)]
    private readonly struct WidePayload(long marker) { public readonly long Marker = marker; }

    private struct Advance : IQueryProcessor<Position>
    {
        public void Execute(Entity entity, ref Position position) => position.Value++;
    }
}
