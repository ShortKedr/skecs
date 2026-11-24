#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif
using SKECS;
using SKECS.GA;
using SKECS.Numerics;
using static FlatCandidates;

internal readonly struct SpatialCase
{
    internal SpatialCase(int rows, uint seed, int chunk, int budget) =>
        (Rows, Seed, Chunk, Budget) = (rows, seed, chunk, budget);

    internal int Rows { get; }
    internal uint Seed { get; }
    internal int Chunk { get; }
    internal int Budget { get; }

    public override string ToString() => $"rows={Rows}, seed={Seed}, chunk={Chunk}, budget={Budget}";
}

[Flags]
internal enum Witness
{
    Duplicate = 1, Overinclusive = 2, Outside = 4, Large = 8, Capacity = 16, PriorityTie = 32, IdentityTie = 64,
    DestroyedParent = 128, Reparented = 256, RefreshedParent = 512, RefreshedTarget = 1024, StaleTarget = 2048,
    ExactLeftEdge = 4096, ExactOneRawPast = 8192,
    All = (1 << 14) - 1
}

internal readonly struct SpatialCoverage
{
    internal SpatialCoverage(long queries, long candidates, long duplicates, long exact, long stale, long batches, Witness witnesses)
    {
        (Queries, Candidates, Duplicates, Exact) = (queries, candidates, duplicates, exact);
        (Stale, Batches, Witnesses) = (stale, batches, witnesses);
    }

    internal long Queries { get; }
    internal long Candidates { get; }
    internal long Duplicates { get; }
    internal long Exact { get; }
    internal long Stale { get; }
    internal long Batches { get; }
    internal Witness Witnesses { get; }

    public static SpatialCoverage operator +(SpatialCoverage a, SpatialCoverage b) => new(a.Queries + b.Queries, a.Candidates + b.Candidates,
        a.Duplicates + b.Duplicates, a.Exact + b.Exact, a.Stale + b.Stale, a.Batches + b.Batches, a.Witnesses | b.Witnesses);
}

internal sealed class SpatialScenario
{
    private static readonly GaSnapshotLayout Layout = new(new[] { GaScalar.Bool, GaScalar.Bool, GaScalar.Fixed, GaScalar.Fixed });
    private readonly SpatialCase _scenario;
    private readonly World _world;
    private readonly Entity[] _turrets;
    private readonly FlatCandidates _index = new();
    private readonly Target[] _targets;
    private Parent _a;
    private Parent _b;
    private readonly Entity _originalParent;
    private SpatialMember? _oldTarget;

    private SpatialScenario(SpatialCase scenario, World world)
    {
        _scenario = scenario;
        _world = world;
        _turrets = new Entity[scenario.Rows];
        if (scenario.Rows == 0)
        {
            _targets = Array.Empty<Target>();
            return;
        }
        var fixtures = new (int Priority, RawBounds Bounds)[]
        {
            (3, Point(-4 * Unit, 0)),
            (1, Point(-4 * Unit + 1, 0)),
            (12, new(-5 * Unit, -Unit, -3 * Unit, Unit)),
            (1, new(-Unit, -Unit, Unit, Unit)),
            (2, Point(4 * Unit, 0)),
            (2, Point(4 * Unit, 0)),
            (10, new(8 * Unit - 1, -Unit, 10 * Unit, Unit)),
            (1, new(20 * Unit, 18 * Unit, 22 * Unit, 20 * Unit)),
            (100, new(long.MinValue + 17, long.MinValue + 17, long.MaxValue - 13, long.MaxValue - 13)),
            (4, new(6 * Unit, 6 * Unit, 7 * Unit, 7 * Unit)),
            (7, new(16 * Unit, 0, 20 * Unit, Unit)),
            (5, new(-50 * Unit, -Unit, -49 * Unit, Unit))
        };
        _targets = new Target[fixtures.Length];
        for (int token = 0; token < fixtures.Length; token++)
            _targets[token] = CreateTarget(token, fixtures[token].Priority, fixtures[token].Bounds);
        _a = CreateParent(0, 0);
        _b = CreateParent(20 * Unit, 19 * Unit);
        _originalParent = _a.Entity;
        for (int row = 0; row < _turrets.Length; row++)
            _turrets[row] = world.CreateEntity(new TurretProbe());
    }

    internal static (SpatialCase[] Cases, int Ticks) SelectCases(string profile)
    {
        if (profile is "short" or "long")
            return (new[] { new SpatialCase(65, 27, 127, 64) }, profile == "short" ? 1_000 : 100_000);
        int[] rows = { 0, 1, 63, 64, 65, 127, 128, 129, 1024 };
        uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
        int[] chunks = { 1, 64, 127, 256 };
        int[] budgets = { 1, 3, 64 };
        if (profile is not ("smoke" or "matrix"))
            throw new ArgumentException($"Unknown SpatialParent profile '{profile}'.");
        var scenarios = new List<SpatialCase>();
        foreach (int rowCount in rows)
        {
            int firstSeed = profile == "matrix" ? 0 : scenarios.Count % seeds.Length;
            int lastSeed = profile == "matrix" ? seeds.Length : firstSeed + 1;
            for (int seed = firstSeed; seed < lastSeed; seed++)
            {
                int i = scenarios.Count;
                scenarios.Add(new SpatialCase(rowCount, seeds[seed], chunks[i % chunks.Length], budgets[i % budgets.Length]));
            }
        }
        return (scenarios.ToArray(), 8);
    }

    internal static async ValueTask<SpatialCoverage> Run(SpatialCase scenario, int ticks, bool useGpu, bool requireYield,
        GaBufferCodec<SpatialMember> codec,
        Action<DeterministicWorkload<byte>, GaTickBuffer, GaTickSnapshot, Action<GaBatchTimings>> register,
        Func<DeterministicRunner<byte>, int, ValueTask<bool>> drain, Action<string> report)
    {
        using var world = new World(new WorldOptions(scenario.Chunk));
        var test = new SpatialScenario(scenario, world);
        var snapshot = new GaTickSnapshot();
        var buffer = new GaTickBuffer();
        var workload = new DeterministicWorkload<byte>();
        long deviceRows = 0;
        long batches = 0;
        register(workload, buffer, snapshot, batch => { batches++; deviceRows += batch.Rows; });
        using var runner = new DeterministicRunner<byte>(world, workload);
        var total = default(SpatialCoverage);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var (expected, packed, witnesses) = test.Prepare(tick, snapshot);
            buffer.Bind(tick, CaptureBuffer(codec, packed));
            runner.BeginTick(tick, 0, Fixed.One);
            bool waited = await drain(runner, scenario.Budget);
            if (requireYield && useGpu && scenario.Rows != 0 && !waited)
                throw new InvalidDataException($"GPU work did not yield at {scenario}, tick {tick}.");
            if (useGpu && deviceRows != (long)scenario.Rows * tick)
                throw new InvalidDataException($"Incomplete device execution at {scenario}, tick {tick}.");
            total += test.Compare(runner.LastCompleted!, tick, expected, witnesses);
            if (tick % 1_000 == 0)
                report($"progress tick={tick}/{ticks} deviceBatches={batches} deviceRows={deviceRows}");
        }
        return new SpatialCoverage(total.Queries, total.Candidates, total.Duplicates, total.Exact, total.Stale, batches, total.Witnesses);
    }

    internal static void VerifyCoverage(SpatialCoverage total, bool useGpu)
    {
        if (total.Witnesses != Witness.All)
            throw new InvalidDataException($"The corpus missed {Witness.All & ~total.Witnesses}.");
        if (total.Queries <= 0 || (useGpu ? total.Batches <= 0 : total.Batches != 0))
            throw new InvalidDataException("The corpus did not execute its selected route.");
    }

    private static GaBufferPacket CaptureBuffer(GaBufferCodec<SpatialMember> codec, List<SpatialMember> packed)
    {
#if NET8_0_OR_GREATER
        var rows = CollectionsMarshal.AsSpan(packed);
#else
        var rows = packed.ToArray().AsSpan();
#endif
        var packet = codec.Capture(rows);
        rows.Clear(); // The captured resource owns its row data.
        return packet;
    }

    private (Expected[] Expected, List<SpatialMember> Packed, Witness Witnesses) Prepare(int tick, GaTickSnapshot snapshot)
    {
        if (_turrets.Length != 0)
            ChangeWorld(tick);
        _index.Clear();
        if (tick == 1) VerifyUnsealed();
        _index.Seal();
        snapshot.Bind(tick, CaptureSnapshotAndIndex(tick));
        _index.Seal();
        var packed = new List<SpatialMember>();
        var expected = new Expected[_turrets.Length];
        var ranges = new Dictionary<RawBounds, Range>();
        var witnesses = (_index.Outside > 0 ? Witness.Outside : 0) |
            (_index.Large > 0 ? Witness.Large : 0) | (_index.Full > 0 ? Witness.Capacity : 0);
        for (int row = 0; row < _turrets.Length; row++)
        {
            var entity = _turrets[row];
            var previous = _world.Get<TurretProbe>(entity);
            var (kind, link, request) = Request(row, tick);
            var (x, y) = _world.IsAlive(link) && _world.TryGet<Mount>(link, out var mount)
                ? (mount.X.Raw + request.OffsetX.Raw, mount.Y.Raw + request.OffsetY.Raw)
                : (request.FallbackX.Raw, request.FallbackY.Raw);
            var box = Around(x, y, request.HalfX.Raw, request.HalfY.Raw);
            if (!ranges.TryGetValue(box, out var range))
            {
                var members = _index.Gather(box, _oldTarget);
                if (members.Length > SpatialKernel.CandidateLimit)
                    throw new InvalidDataException("Spatial fixture exceeded its portable candidate range.");
                range = new Range(packed.Count, members);
                ranges.Add(box, range);
                packed.AddRange(members);
            }
            request.FirstCandidate = range.Start;
            request.Candidates = range.Members.Length;
            _world.Set(entity, request);
            expected[row] = Oracle(request, range, kind, previous, tick);
        }
        return (expected, packed, witnesses);
    }

    private GaSnapshotPacket CaptureSnapshotAndIndex(int tick)
    {
        var keys = new List<GaSnapshotKey>();
        var words = new List<uint>();
        var checkpoint = _world.OpenCheckpoint();
        bool checkedAdd = false;
        for (int id = 0; id < checkpoint.SlotCount; id++)
        {
            var slot = checkpoint.GetSlot(id);
            if (!slot.Alive) continue;
            var entity = _world.BindEntity(id, slot.Version);
            if (checkpoint.TryGet<IndexedTarget>(entity, out var target))
            {
                _index.Add(new SpatialMember
                {
                    Id = id, Generation = slot.Version, Token = target.Token, Priority = target.Priority, Bounds = target.Bounds
                });
                if (tick == 1 && !checkedAdd)
                {
                    VerifyUnsealed(); // Add invalidates an already sealed candidate resource.
                    checkedAdd = true;
                }
            }
            bool isParent = checkpoint.TryGet<Mount>(entity, out var mount);
            keys.Add(new GaSnapshotKey(id, slot.Version));
            words.Add(1u);
            words.Add(isParent ? 1u : 0u);
            WriteRaw(isParent ? mount.X.Raw : 0);
            WriteRaw(isParent ? mount.Y.Raw : 0);
        }
#if NET8_0_OR_GREATER
        return GaSnapshotPacket.FromRows(Layout, CollectionsMarshal.AsSpan(keys), CollectionsMarshal.AsSpan(words));
#else
        return GaSnapshotPacket.FromRows(Layout, keys.ToArray(), words.ToArray());
#endif

        void WriteRaw(long value)
        {
            ulong bits = unchecked((ulong)value);
            words.Add((uint)bits);
            words.Add((uint)(bits >> 32));
        }
    }

    private void VerifyUnsealed()
    {
        try { _index.Gather(Point(0, 0), null); }
        catch (InvalidOperationException) { return; }
        throw new InvalidDataException("An unsealed spatial resource could be queried.");
    }

    private Expected Oracle(TurretProbe query, Range range, int kind, TurretProbe previous, int tick)
    {
        Parent? parent = null;
        if (Matches(_a, query)) parent = _a;
        else if (Matches(_b, query)) parent = _b;
        long x = parent.HasValue ? parent.Value.X + query.OffsetX.Raw : query.FallbackX.Raw;
        long y = parent.HasValue ? parent.Value.Y + query.OffsetY.Raw : query.FallbackY.Raw;
        // Scan the complete logical target set. This oracle does not use the grid to decide any exact overlap.
        var exact = _targets.Where(target => target.Alive &&
                target.Bounds.Right >= x - query.HalfX.Raw && target.Bounds.Left <= x + query.HalfX.Raw &&
                target.Bounds.Top >= y - query.HalfY.Raw && target.Bounds.Bottom <= y + query.HalfY.Raw)
            .Select(target => (Target: target, Score: Score(target.Bounds, x, y)))
            .OrderBy(hit => hit.Score).ThenBy(hit => hit.Target.Priority)
            .ThenBy(hit => hit.Target.Entity.Id).ThenBy(hit => hit.Target.Entity.Version).ToArray();

        uint exactSet = 0;
        foreach (var (target, _) in exact)
        {
            if (!range.Members.Any(member => member.Id == target.Entity.Id && member.Generation == target.Entity.Version))
                throw new InvalidDataException($"Grid false negative at {_scenario}, tick {tick}, target {target.Entity.Id}.");
            exactSet |= 1u << target.Token;
        }
        var distinct = range.Members.Select(member => (member.Id, member.Generation)).Distinct().ToArray();
        int duplicates = range.Members.Length - distinct.Length;
        int stale = distinct.Count(key => !_targets.Any(target =>
            target.Alive && target.Entity.Id == key.Id && target.Entity.Version == key.Generation));
        uint stamp = 2166136261u;
        foreach (var member in range.Members)
        {
            ulong mix = ((ulong)stamp * 16777619u) & uint.MaxValue;
            stamp = (uint)(mix ^ (ulong)member.Id ^ ((ulong)member.Generation * 65536u));
        }

        var best = exact.FirstOrDefault();
        var witnesses = (duplicates > 0 ? Witness.Duplicate : 0) |
            (distinct.Length - stale > exact.Length ? Witness.Overinclusive : 0) |
            (stale > 0 ? Witness.StaleTarget : 0);
        if (exact.Any(candidate => candidate.Score == best.Score && candidate.Target.Priority != best.Target.Priority))
            witnesses |= Witness.PriorityTie;
        if (exact.Skip(1).Any(candidate => candidate.Score == best.Score && candidate.Target.Priority == best.Target.Priority))
            witnesses |= Witness.IdentityTie;
        if (tick is 3 or 4 && query.ParentId == _originalParent.Id && !parent.HasValue)
            witnesses |= Witness.DestroyedParent;
        if (tick == 6 && kind == 4 && parent.HasValue && previous.ParentId == _originalParent.Id &&
            previous.ParentGeneration == _originalParent.Version && query.ParentGeneration != previous.ParentGeneration)
            witnesses |= Witness.Reparented;
        if (tick == 2 && kind == 2 && parent.HasValue && x == 24 * Unit)
            witnesses |= Witness.RefreshedParent;
        if (tick == 7 && kind == 5 && (exactSet & (1u << 10)) != 0)
            witnesses |= Witness.RefreshedTarget;
        if (kind == 1 && (exactSet & 3) == 1) witnesses |= Witness.ExactLeftEdge;
        if (kind == 5 && (exactSet & 3) == 2) witnesses |= Witness.ExactOneRawPast;
        return new Expected(parent.HasValue, x, y, range.Members.Length, duplicates, stale, stamp, exactSet, exact.Length,
            exact.Length == 0 ? -1 : best.Target.Entity.Id, exact.Length == 0 ? 0 : best.Target.Entity.Version,
            exact.Length == 0 ? 0 : best.Target.Priority, best.Score, witnesses);
    }

    private SpatialCoverage Compare(DeterministicTickResult result, int tick, Expected[] expected, Witness witnesses)
    {
        if (result.TickIndex != tick || result.Events.Count != _turrets.Length)
            throw new InvalidDataException($"Wrong tick or event count at {_scenario}, tick {tick}.");
        int bufferRows = 0;
        if (expected.Length != 0)
            bufferRows = _turrets.Select(entity => _world.Get<TurretProbe>(entity)).Max(row => row.FirstCandidate + row.Candidates);
        var totals = new SpatialCoverage(0, 0, 0, 0, 0, 0, witnesses);
        for (int row = 0; row < _turrets.Length; row++)
        {
            var item = result.Events[row];
            var high = item.Get<SpatialReport>();
            var low = _world.Get<TurretProbe>(_turrets[row]);
            var want = expected[row];
            if (item.Key is not { Stage: SystemStage.Update, SystemOrder: 0, ProducerOrder: 0,
                    SourceKind: DeterministicSourceKind.Entity, LocalOrdinal: 0, DomainKey: null } ||
                item.Key.TickIndex != tick || item.Key.SourceId != _turrets[row].Id || item.Key.SourceGeneration != _turrets[row].Version ||
                high.ParentId != low.ParentId || high.ParentGeneration != low.ParentGeneration || high.ParentAvailable != want.Parent ||
                high.WorldX.Raw != want.X || high.WorldY.Raw != want.Y || high.BufferRows != bufferRows ||
                high.CandidateCount != want.Candidates || high.DuplicateCount != want.Duplicates || high.StaleCount != want.Stale ||
                high.CandidateStamp != want.Stamp || high.ExactSet != want.ExactSet || high.ExactCount != want.ExactCount ||
                high.SelectedId != want.TargetId || high.SelectedGeneration != want.TargetGeneration ||
                high.SelectedPriority != want.Priority || high.Score.Raw != want.Score ||
                low.WorldX.Raw != want.X || low.WorldY.Raw != want.Y || low.ExactSet != want.ExactSet ||
                low.ExactCount != want.ExactCount || low.SelectedId != want.TargetId || low.SelectedGeneration != want.TargetGeneration)
                throw new InvalidDataException($"Spatial result diverged at {_scenario}, tick {tick}, row {row}: expected {want}, " +
                    $"actual parent={high.ParentAvailable} center=({high.WorldX.Raw},{high.WorldY.Raw}) " +
                    $"candidate={high.CandidateCount}/{high.DuplicateCount}/{high.StaleCount}/{high.CandidateStamp} " +
                    $"exact={high.ExactSet:x}/{high.ExactCount} winner={high.SelectedId}/{high.SelectedGeneration}/{high.Score.Raw}.");
            totals += new SpatialCoverage(1, want.Candidates, want.Duplicates, want.ExactCount, want.Stale, 0, want.Witnesses);
        }
        return totals;
    }

    private (int Kind, Entity Link, TurretProbe Probe) Request(int row, int tick)
    {
        int kind = row == 0 && tick is >= 4 and <= 6 ? 4 : (int)((row + tick - 1 + (_scenario.Seed & 7)) % 8);
        var (link, dx, dy, fallbackX, fallbackY, halfX, halfY) = kind switch
        {
            0 => (_originalParent, 4 * Unit, 0L, 4 * Unit, 0L, 0L, 0L),
            1 => (_originalParent, -4 * Unit, 0L, -4 * Unit, 0L, 0L, 0L),
            2 => (_b.Entity, 0L, 0L, 24 * Unit, 19 * Unit, Unit, Unit),
            3 => (_originalParent, 0L, Unit, 10 * Unit, -Unit, 5 * Unit, 2 * Unit),
            4 => (tick < 6 ? _originalParent : _a.Entity, 0L, 0L, -Unit, 0L, 6 * Unit, 2 * Unit),
            5 => (_targets[0].Entity, 0L, 0L, -4 * Unit + 1, 0L, 0L, 0L),
            6 => (_b.Entity, 0L, 0L, 24 * Unit, 19 * Unit, 180 * Unit, 180 * Unit),
            _ => (_a.Entity, -4 * Unit - 1, 0L, -4 * Unit - 1, 0L, 0L, 0L)
        };
        return (kind, link, new TurretProbe
        {
            ParentId = link.Id, ParentGeneration = link.Version, OffsetX = Fixed.FromRaw(dx), OffsetY = Fixed.FromRaw(dy),
            FallbackX = Fixed.FromRaw(fallbackX), FallbackY = Fixed.FromRaw(fallbackY),
            HalfX = Fixed.FromRaw(halfX), HalfY = Fixed.FromRaw(halfY)
        });
    }

    private void ChangeWorld(int tick)
    {
        switch (tick)
        {
            case 2:
                _b.X = 24 * Unit;
                _world.Get<Mount>(_b.Entity).X = Fixed.FromRaw(_b.X);
                break;
            case 3:
                _world.DestroyEntity(_a.Entity);
                _a.Alive = false;
                var old = _targets[10];
                _oldTarget = new SpatialMember
                {
                    Id = old.Entity.Id, Generation = old.Entity.Version,
                    Token = old.Token, Priority = old.Priority, Bounds = old.Bounds.ToComponent()
                };
                _world.DestroyEntity(old.Entity);
                _targets[10].Alive = false;
                break;
            case 4:
                var target = CreateTarget(10, 7, Point(4 * Unit, 0));
                CheckReuse(_targets[10].Entity, target.Entity);
                _targets[10] = target;
                break;
            case 5:
                _a = CreateParent(Unit, 0);
                CheckReuse(_originalParent, _a.Entity);
                break;
            case 6:
                _a.X = Unit + 1;
                _world.Get<Mount>(_a.Entity).X = Fixed.FromRaw(_a.X);
                break;
            case 7:
                ref var moved = ref _targets[10];
                moved.Bounds = Point(-4 * Unit + 1, 0);
                _world.Get<IndexedTarget>(moved.Entity).Bounds = moved.Bounds.ToComponent();
                break;
        }
    }

    private Target CreateTarget(int token, int priority, RawBounds bounds) =>
        new(_world.CreateEntity(new IndexedTarget { Token = token, Priority = priority, Bounds = bounds.ToComponent() }),
            true, token, priority, bounds);

    private Parent CreateParent(long x, long y) =>
        new(_world.CreateEntity(new Mount { X = Fixed.FromRaw(x), Y = Fixed.FromRaw(y) }), true, x, y);

    private static bool Matches(Parent parent, TurretProbe query) =>
        parent.Alive && parent.Entity.Id == query.ParentId && parent.Entity.Version == query.ParentGeneration;

    private static RawBounds Point(long x, long y) => new(x, y, x, y);
    private static RawBounds Around(long x, long y, long halfX, long halfY) => new(x - halfX, y - halfY, x + halfX, y + halfY);
    private static long Score(RawBounds box, long x, long y) =>
        Math.Abs(x - Math.Clamp(x, box.Left, box.Right)) + Math.Abs(y - Math.Clamp(y, box.Bottom, box.Top));

    private void CheckReuse(Entity previous, Entity current)
    {
        if (previous.Id != current.Id || previous.Version >= current.Version || _world.IsAlive(previous) || !_world.IsAlive(current))
            throw new InvalidDataException("The world did not reuse the intended identity.");
    }

    private struct Target
    {
        internal Target(Entity entity, bool alive, int token, int priority, RawBounds bounds) =>
            (Entity, Alive, Token, Priority, Bounds) = (entity, alive, token, priority, bounds);

        internal Entity Entity;
        internal bool Alive;
        internal int Token;
        internal int Priority;
        internal RawBounds Bounds;
    }

    private struct Parent
    {
        internal Parent(Entity entity, bool alive, long x, long y) => (Entity, Alive, X, Y) = (entity, alive, x, y);
        internal Entity Entity;
        internal bool Alive;
        internal long X;
        internal long Y;
    }

    private readonly struct Range
    {
        internal Range(int start, SpatialMember[] members) => (Start, Members) = (start, members);
        internal int Start { get; }
        internal SpatialMember[] Members { get; }
    }

    private readonly struct Expected
    {
        internal Expected(bool parent, long x, long y, int candidates, int duplicates, int stale,
            uint stamp, uint exactSet, int exactCount, int targetId, int targetGeneration, int priority, long score, Witness witnesses)
        {
            (Parent, X, Y, Candidates, Duplicates, Stale) = (parent, x, y, candidates, duplicates, stale);
            (Stamp, ExactSet, ExactCount, TargetId, TargetGeneration) = (stamp, exactSet, exactCount, targetId, targetGeneration);
            (Priority, Score, Witnesses) = (priority, score, witnesses);
        }

        internal bool Parent { get; }
        internal long X { get; }
        internal long Y { get; }
        internal int Candidates { get; }
        internal int Duplicates { get; }
        internal int Stale { get; }
        internal uint Stamp { get; }
        internal uint ExactSet { get; }
        internal int ExactCount { get; }
        internal int TargetId { get; }
        internal int TargetGeneration { get; }
        internal int Priority { get; }
        internal long Score { get; }
        internal Witness Witnesses { get; }

        public override string ToString() => $"parent={Parent} center=({X},{Y}) candidate={Candidates}/{Duplicates}/{Stale}/{Stamp} " +
            $"exact={ExactSet:x}/{ExactCount} winner={TargetId}/{TargetGeneration}/{Score}";
    }
    private struct Mount { public Fixed X, Y; }
    private struct IndexedTarget { public int Token, Priority; public SpatialBounds Bounds; }
}
