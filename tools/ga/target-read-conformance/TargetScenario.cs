#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

internal readonly struct TargetCase
{
    internal TargetCase(int rows, uint seed, int chunk, int budget, int workers) =>
        (Rows, Seed, Chunk, Budget, Workers) = (rows, seed, chunk, budget, workers);

    internal int Rows { get; }
    internal uint Seed { get; }
    internal int Chunk { get; }
    internal int Budget { get; }
    internal int Workers { get; }

    public override string ToString() => $"rows={Rows}, seed={Seed}, chunk={Chunk}, budget={Budget}, workers={Workers}";
}

internal readonly struct TargetCoverage
{
    internal TargetCoverage(long rows, long self, long other, long missingTag, long destroyed,
        long stale, long reused, long bodyless, long deviceRows, long deviceBatches)
    {
        (Rows, Self, Other, MissingTag, Destroyed) = (rows, self, other, missingTag, destroyed);
        (Stale, Reused, Bodyless, DeviceRows, DeviceBatches) = (stale, reused, bodyless, deviceRows, deviceBatches);
    }

    internal long Rows { get; }
    internal long Self { get; }
    internal long Other { get; }
    internal long MissingTag { get; }
    internal long Destroyed { get; }
    internal long Stale { get; }
    internal long Reused { get; }
    internal long Bodyless { get; }
    internal long DeviceRows { get; }
    internal long DeviceBatches { get; }

    public static TargetCoverage operator +(TargetCoverage left, TargetCoverage right) => new(
        left.Rows + right.Rows, left.Self + right.Self, left.Other + right.Other, left.MissingTag + right.MissingTag,
        left.Destroyed + right.Destroyed, left.Stale + right.Stale, left.Reused + right.Reused, left.Bodyless + right.Bodyless,
        left.DeviceRows + right.DeviceRows, left.DeviceBatches + right.DeviceBatches);
}

internal sealed class TargetScenario
{
    private const long MissingPosition = -0x112233445566L;
    private static readonly GaSnapshotLayout Layout = new(new[] { GaScalar.Bool, GaScalar.Bool, GaScalar.Fixed });
    private readonly TargetCase _scenario;
    private readonly Entity[] _sources;
    private readonly long[] _positions;
    private readonly long[] _steps;
    private readonly long[] _next;
    private readonly long[] _counts = new long[8];
    private Entity _tagged;
    private Entity _untagged;
    private Entity _bodyless;
    private Entity _destroyed;
    private Entity _recycled;
    private int _generation;
    private long _recycledPosition;
    private bool _taggedPresent = true;

    private TargetScenario(TargetCase scenario, World world)
    {
        _scenario = scenario;
        _sources = new Entity[scenario.Rows];
        _positions = new long[scenario.Rows];
        _steps = new long[scenario.Rows];
        _next = new long[scenario.Rows];
        for (int row = 0; row < scenario.Rows; row++)
        {
            // Exercise both sign bits and the two words of Q32.32 values.
            long raw = (row + 1) * 1_000_000_019L + (scenario.Seed & 127);
            _positions[row] = row % 2 == 0 ? raw : -raw;
            _steps[row] = 4_294_967_297L + 17L * row + (scenario.Seed & 255);
            _sources[row] = world.CreateEntity(new TargetBody
            {
                Position = Fixed.FromRaw(_positions[row]),
                Step = Fixed.FromRaw(_steps[row])
            }, new TargetLink());
            CheckId(_sources[row], row, 1);
            if (row % 2 == 0)
                world.Add(_sources[row], new TrackingTag());
        }
        if (scenario.Rows == 0)
            return;
        _tagged = world.CreateEntity(Body(TaggedPosition), new TrackingTag());
        _untagged = world.CreateEntity(Body(UntaggedPosition));
        _bodyless = world.CreateEntity();
        _destroyed = world.CreateEntity(Body(3), new TrackingTag());
        _recycled = world.CreateEntity(Body(5), new TrackingTag());
        CheckId(_tagged, scenario.Rows, 1);
        CheckId(_untagged, scenario.Rows + 1, 1);
        CheckId(_bodyless, scenario.Rows + 2, 1);
        CheckId(_destroyed, scenario.Rows + 3, 1);
        CheckId(_recycled, scenario.Rows + 4, 1);
        world.DestroyEntity(_destroyed);
        _generation = 1;
        Recycle(world);
    }

    private long TaggedPosition => 0x1122334455L + (_scenario.Seed & 255);
    private long UntaggedPosition => -0x5566778899L - (_scenario.Seed & 255);
    private static TargetBody Body(long position) => new() { Position = Fixed.FromRaw(position) };

    internal static (TargetCase[] Cases, int Ticks) SelectCases(string profile, bool singleWorker)
    {
        int[] rows = { 0, 1, 63, 64, 65, 127, 128, 129, 1024 };
        uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
        int[] chunks = { 1, 64, 127, 256 };
        int[] budgets = { 1, 3, 64 };
        int[] workers = singleWorker ? new[] { 1 } : new[] { 1, 2, Environment.ProcessorCount };
        if (profile is "short" or "long")
            return (new[] { new TargetCase(65, 27, 127, 64, singleWorker ? 1 : 2) }, profile == "short" ? 1_000 : 100_000);
        if (profile is not ("smoke" or "matrix"))
            throw new ArgumentException($"Unknown TargetRead profile '{profile}'.");
        var scenarios = new List<TargetCase>();
        foreach (int rowCount in rows)
        {
            int firstSeed = profile == "matrix" ? 0 : scenarios.Count % seeds.Length;
            int lastSeed = profile == "matrix" ? seeds.Length : firstSeed + 1;
            for (int seed = firstSeed; seed < lastSeed; seed++)
            {
                int i = scenarios.Count;
                scenarios.Add(new TargetCase(rowCount, seeds[seed], chunks[i % chunks.Length],
                    budgets[i % budgets.Length], workers[i % workers.Length]));
            }
        }
        return (scenarios.ToArray(), 8);
    }

    internal static async ValueTask<TargetCoverage> Run(TargetCase scenario, int ticks, bool useGpu, bool requireYield,
        Action<DeterministicWorkload<byte>, GaTickSnapshot, Action<GaBatchTimings>> register,
        Func<DeterministicRunner<byte>, int, ValueTask<bool>> drain, Action<string> report)
    {
        using var world = new World(new WorldOptions(scenario.Chunk));
        var test = new TargetScenario(scenario, world);
        var snapshot = new GaTickSnapshot();
        var workload = new DeterministicWorkload<byte>();
        long deviceRows = 0;
        long deviceBatches = 0;
        register(workload, snapshot, batch => { deviceRows += batch.Rows; deviceBatches++; });

        using var runner = new DeterministicRunner<byte>(world, workload, scenario.Workers);
        for (int tick = 1; tick <= ticks; tick++)
        {
            if (scenario.Rows != 0)
                test.PrepareWorld(world, tick);
            var packet = Capture(world);
            if (packet.RowCount != (scenario.Rows == 0 ? 0 : scenario.Rows + 4))
                throw new InvalidDataException("Snapshot membership differs from the live fixture.");
            snapshot.Bind(tick, packet);
            runner.BeginTick(tick, 0, Fixed.One);
            bool waited = await drain(runner, scenario.Budget);
            if (requireYield && useGpu && scenario.Rows != 0 && !waited)
                throw new InvalidDataException($"GPU work did not yield at {scenario}, tick {tick}.");
            if (useGpu && deviceRows != (long)scenario.Rows * tick)
                throw new InvalidDataException($"GPU rows differ at {scenario}, tick {tick}.");
            test.VerifyTick(world, runner.LastCompleted!, tick);
            if (ticks >= 1_000 && tick % 1_000 == 0)
                report($"progress tick={tick}/{ticks} deviceRows={deviceRows} deviceBatches={deviceBatches}");
        }
        return new TargetCoverage((long)scenario.Rows * ticks, test._counts[0], test._counts[1], test._counts[3],
            test._counts[4], test._counts[5], test._counts[6], test._counts[7], deviceRows, deviceBatches);
    }

    internal static void VerifyCoverage(TargetCoverage coverage, bool useGpu)
    {
        if (coverage.Rows <= 0 || coverage.Self <= 0 || coverage.Other <= 0 || coverage.MissingTag <= 0 ||
            coverage.Destroyed <= 0 || coverage.Stale <= 0 || coverage.Reused <= 0 || coverage.Bodyless <= 0)
            throw new InvalidDataException("The corpus missed a required target mode.");
        if (useGpu ? coverage.DeviceRows != coverage.Rows || coverage.DeviceBatches <= 0 :
            coverage.DeviceRows != 0 || coverage.DeviceBatches != 0)
            throw new InvalidDataException("Actual device work did not match expected routes.");
    }

    private void PrepareWorld(World world, int tick)
    {
        switch (tick % 8)
        {
            case 3:
                world.Remove<TrackingTag>(_tagged);
                _taggedPresent = false;
                break;
            case 4:
                // An external ref write must be captured even without a Set call.
                _positions[0] += 4099;
                world.Get<TargetBody>(_sources[0]).Position = Fixed.FromRaw(_positions[0]);
                break;
            case 5:
                world.Add(_tagged, new TrackingTag());
                _taggedPresent = true;
                Recycle(world);
                break;
        }
        for (int row = 0; row < _sources.Length; row++)
            world.Set(_sources[row], Link(row, Phase(row, tick)));
    }

    private void Recycle(World world)
    {
        var stale = _recycled;
        world.DestroyEntity(stale);
        _generation++;
        _recycledPosition = -0x76543210FEDL + _generation * 131L;
        _recycled = world.CreateEntity(Body(_recycledPosition), new TrackingTag());
        CheckId(_recycled, _scenario.Rows + 4, _generation);
        if (world.IsAlive(stale) || !world.IsAlive(_recycled) || world.IsAlive(_destroyed))
            throw new InvalidDataException("World did not retain the intended dead and reused generations.");
    }

    private int Phase(int row, int tick)
    {
        // Guarantee that the externally edited Position is observed through the newly bound packet.
        if (row == 0 && tick % 8 == 4)
            return 0;
        return (int)(((long)row + tick - 1 + (_scenario.Seed & 7)) % 8);
    }

    private TargetLink Link(int row, int phase)
    {
        Entity target = phase switch
        {
            0 => _sources[row],
            1 => _sources.Length == 1 ? _tagged : _sources[(row + _sources.Length - 1) % _sources.Length],
            2 => _tagged,
            3 => _untagged,
            4 => _destroyed,
            5 or 6 => _recycled,
            _ => _bodyless
        };
        return new TargetLink { Id = target.Id, Generation = phase == 5 ? target.Version - 1 : target.Version };
    }

    private static GaSnapshotPacket Capture(World world)
    {
        var checkpoint = world.OpenCheckpoint();
        var keys = new List<GaSnapshotKey>();
        var words = new List<uint>();
        // Prepare deliberately descending IDs; the packet canonicalizes them.
        for (int id = checkpoint.SlotCount - 1; id >= 0; id--)
        {
            var slot = checkpoint.GetSlot(id);
            if (!slot.Alive)
                continue;
            var entity = world.BindEntity(id, slot.Version);
            keys.Add(new GaSnapshotKey(id, slot.Version));
            words.Add(1);
            words.Add(world.Has<TrackingTag>(entity) ? 1u : 0u);
            long value = checkpoint.TryGet<TargetBody>(entity, out var target) ? target.Position.Raw : MissingPosition;
            ulong raw = unchecked((ulong)value);
            words.Add((uint)raw);
            words.Add((uint)(raw >> 32));
        }
#if NET8_0_OR_GREATER
        return GaSnapshotPacket.FromRows(Layout, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(keys),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(words));
#else
        return GaSnapshotPacket.FromRows(Layout, keys.ToArray(), words.ToArray());
#endif
    }

    private void VerifyTick(World world, DeterministicTickResult result, int tick)
    {
        if (result.TickIndex != tick || result.Events.Count != _sources.Length)
            throw new InvalidDataException($"Unexpected event count at {_scenario}, tick {tick}.");
        for (int row = 0; row < _sources.Length; row++)
        {
            int phase = Phase(row, tick);
            _counts[phase]++;
            var link = Link(row, phase);
            // The oracle uses scripted membership and its own previous-tick positions, never snapshot words.
            var (alive, hasTag, previous) = phase switch
            {
                0 => (true, row % 2 == 0, _positions[row]),
                1 when _sources.Length == 1 => (true, _taggedPresent, TaggedPosition),
                1 => (true, link.Id % 2 == 0, _positions[link.Id]),
                2 => (true, _taggedPresent, TaggedPosition),
                3 => (true, false, UntaggedPosition),
                4 or 5 => (false, false, MissingPosition),
                6 => (true, true, _recycledPosition),
                _ => (true, false, MissingPosition)
            };
            long expected = checked((hasTag ? previous : _positions[row]) + _steps[row]);
            _next[row] = expected;
            var actual = result.Events[row];
            var observation = actual.Get<TargetObservation>();
            var body = world.Get<TargetBody>(_sources[row]);
            var persistedLink = world.Get<TargetLink>(_sources[row]);
            var key = actual.Key;
            if (key is not { Stage: SystemStage.Update, SystemOrder: 0, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Entity,
                    SourceGeneration: 1, LocalOrdinal: 0, DomainKey: null } || key.TickIndex != tick || key.SourceId != row ||
                observation.Id != link.Id || observation.Generation != link.Generation || observation.Alive != alive ||
                observation.HasTag != hasTag || observation.PreviousPosition.Raw != previous || observation.Position.Raw != expected ||
                body.Position.Raw != expected || body.Step.Raw != _steps[row] ||
                persistedLink.Id != link.Id || persistedLink.Generation != link.Generation)
                throw new InvalidDataException($"Target observation diverged at {_scenario}, tick {tick}, source {row}, " +
                    $"phase {phase}, target {link.Id}/{link.Generation}: expected ({alive},{hasTag},{previous},{expected}), " +
                    $"actual ({observation.Alive},{observation.HasTag},{observation.PreviousPosition.Raw},{observation.Position.Raw}), " +
                    $"world position {body.Position.Raw}, step {body.Step.Raw}, link {persistedLink.Id}/{persistedLink.Generation}.");
        }
        _next.CopyTo(_positions, 0);
    }

    private static void CheckId(Entity actual, int id, int generation)
    {
        if (actual.Id != id || actual.Version != generation)
            throw new InvalidDataException($"Entity allocation differed: expected {id}/{generation}, got {actual.Id}/{actual.Version}.");
    }

    private struct TrackingTag { }
}
