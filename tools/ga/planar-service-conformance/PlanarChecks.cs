using System.Diagnostics;
using System.Security.Cryptography;
using SKECS;
using SKECS.Numerics;
using SKECS.Serialization;

internal static class PlanarChecks
{
    private const int ForkTick = 3;
    private const int FinalTick = 12;
    private static readonly Fixed Delta = Fixed.FromRatio(1, 16);
    private static readonly CodecRegistry Registry = new CodecRegistry()
        .Register(new ComponentSchema(311, 1, new FieldSchema(1, FieldKind.Int64), new FieldSchema(2, FieldKind.Int64),
            new FieldSchema(3, FieldKind.Int64), new FieldSchema(4, FieldKind.Int64)), new MotionCodec())
        .Register(new ComponentSchema(312, 1, new FieldSchema(1, FieldKind.Int32), new FieldSchema(2, FieldKind.Int32)),
            new LinkCodec());

    internal readonly record struct Frame(long Tick, int Rows, int Impulses, PlanarPair[] Began, byte[] Checkpoint);
    internal readonly record struct RunResult(byte[] InitialCheckpoint, Frame[] Frames, int ProcessedRows);

    internal static void VerifyNumerics()
    {
        PlanarOracle.VerifyArithmetic();
        var diagonal = Solve(
            [Sample(0, 0, 0, 1, 1), Sample(1, 1, 1, 0, 0)]);
        Require(diagonal is { Impulses: 1, Pairs.Length: 1 } &&
            diagonal.Rows.SequenceEqual(new[] { Sample(0, 0, 0, 0, 0), Sample(1, 1, 1, 1, 1) }),
            "Diagonal contact did not exchange the projected velocity.");
        Require(Solve([Sample(0, 0, 0, 0, 0), Sample(1, 2, 1, 0, 0)]).Pairs.Length == 0,
            "Box overlap was mistaken for a circle contact.");
        Require(Solve([Sample(0, 0, 0, 0, 0), Sample(1, 0, 2, 0, 0)]).Pairs.Length == 1,
            "Exact circle tangency was excluded.");
        foreach (long raw in new[] { 0L, 1L })
        {
            var exact = Solve([Sample(0, 0, 0, 1, 2), Sample(1, 0, 0, 0, -1) with { Y = Fixed.FromRaw(raw) }]);
            Require(exact.Impulses == 1 && exact.Rows[0].Vx == Fixed.Zero && exact.Rows[0].Vy == Fixed.FromInt32(2) &&
                exact.Rows[1].Vx == Fixed.One && exact.Rows[1].Vy == Fixed.FromInt32(-1),
                "Coincident or quantized-zero distance did not use the deterministic +X normal.");
        }
        var ordered = Solve([Sample(0, 0, 0, 2, 1), Sample(1, 1, 0, 0, 0), Sample(2, 1, 1, -1, -1)]);
        Require(ordered.Impulses == 2 && ordered.Pairs.SequenceEqual(new[] { new PlanarPair(0, 1, 1, 1),
            new PlanarPair(0, 1, 2, 1), new PlanarPair(1, 1, 2, 1) }) &&
            ordered.Rows[0].Vx == Fixed.FromRatio(-3, 2) && ordered.Rows[0].Vy == Fixed.FromRatio(-1, 2) &&
            ordered.Rows[1].Vx == Fixed.FromInt32(2) && ordered.Rows[1].Vy == Fixed.Zero &&
            ordered.Rows[2].Vx == Fixed.FromRatio(1, 2) && ordered.Rows[2].Vy == Fixed.FromRatio(1, 2),
            "Sequential pair order changed the cumulative impulse.");

        uint random = 0x8c53ba97;
        const int numericCases = 1024;
        for (int index = 0; index < numericCases; index++)
        {
            var rows = new PlanarSample[2 + Next(4)];
            for (int slot = 0; slot < rows.Length; slot++)
                rows[slot] = new(slot, 1, Coordinate(), Coordinate(), Coordinate(), Coordinate());
            Solve(rows);
        }
        Console.WriteLine($"diagProjection=passed circleBoundary=passed zeroNormal=passed pairOrder=passed numericCases={numericCases}");

        Fixed Coordinate() => Fixed.FromRatio(Next(33) - 16, 8);
        int Next(uint bound)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (int)(random % bound);
        }

        static PlanarResult Solve(PlanarSample[] input)
        {
            var actual = PlanarContacts.Solve(input);
            var oracle = PlanarOracle.Solve(input);
            Require(actual.Rows.SequenceEqual(oracle.Rows) && actual.Pairs.SequenceEqual(oracle.Pairs) &&
                actual.Impulses == oracle.Impulses, "Planar solver disagrees with BigInteger geometry.");
            return actual;
        }

        static PlanarSample Sample(int slot, int x, int y, int vx, int vy) =>
            new(slot, 1, Fixed.FromInt32(x), Fixed.FromInt32(y), Fixed.FromInt32(vx), Fixed.FromInt32(vy));
    }

    internal static RunResult Run(Func<PlanarState, DeterministicWorkload<PlanarFrame>> plan, string lane, int chunk)
    {
        using var world = new World(new WorldOptions(chunk));
        var state = PlanarState.Seed(world);
        byte[] initial = Save(world, state.Capture());
        using (var decoded = Load(initial, chunk, out var initialState))
            Require(initial.AsSpan().SequenceEqual(Save(decoded, initialState.Capture())), "Initial planar checkpoint changed upon restore.");
        using var runner = new DeterministicRunner<PlanarFrame>(world, plan(state), workerCount: 2);
        var frames = new List<Frame>(FinalTick);
        for (int tick = 1; tick <= ForkTick; tick++)
            frames.Add(Advance(world, state, runner, tick));
        Require(frames[0].Impulses >= 2 && state.History.Count >= 2 && state.Free.SequenceEqual(new[] { 3, 4 }),
            "Checkpoint missed ordered impulses, history, or free slots.");
        byte[] fork = frames[^1].Checkpoint;
        byte[] withoutHistory = Save(world, state.Capture(includeHistory: false));
        for (int tick = ForkTick + 1; tick <= FinalTick; tick++)
            frames.Add(Advance(world, state, runner, tick));
        var reusedPair = new PlanarPair(3, 2, 4, 2);
        Require(frames[ForkTick].Began.Contains(reusedPair) && !frames[ForkTick + 1].Began.Contains(reusedPair),
            "The reused pair did not emit exactly one new begin-contact.");

        using var restored = Load(fork, chunk, out var restoredState);
        Require(fork.AsSpan().SequenceEqual(Save(restored, restoredState.Capture())), "Checkpoint changed upon restore.");
        Require(restoredState.LastTickImpulseCount == 0, "A restored service starts with fresh per-tick diagnostics.");
        using var restoredRunner = new DeterministicRunner<PlanarFrame>(restored, plan(restoredState), workerCount: 1);
        var replay = new List<Frame>(FinalTick - ForkTick);
        for (int tick = ForkTick + 1; tick <= FinalTick; tick++)
            replay.Add(Advance(restored, restoredState, restoredRunner, tick));
        Compare(frames.Skip(ForkTick).ToArray(), replay);
        var finalRows = restoredState.CaptureRows(restored);
        Require(finalRows.Single(row => row.Slot == 4) is { Generation: 2 } first &&
                finalRows.Single(row => row.Slot == 3) is { Generation: 2 } second &&
                first.X == Fixed.FromInt32(20) && second.X == Fixed.FromInt32(22),
            "Restored body slots did not reuse in LIFO order with new generations.");

        using var broken = Load(withoutHistory, chunk, out var noHistory);
        using var brokenRunner = new DeterministicRunner<PlanarFrame>(broken, plan(noHistory));
        var falseBegins = Advance(broken, noHistory, brokenRunner, ForkTick + 1);
        Require(falseBegins.Began.Except(frames[ForkTick].Began).Any(), "Dropping contact history was not observable.");

        using var badLink = Load(fork, chunk, out var badState);
        var owner = badState.GetOwner(0);
        var entity = badLink.BindEntity(owner.Entity, owner.EntityVersion);
        badLink.Get<PlanarLink>(entity).Generation++;
        byte[] corrupted = Save(badLink, badState.Capture());
        bool rejected = false;
        try { using var invalid = Load(corrupted, chunk, out _); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Restore accepted a stale PlanarLink.");

        badState.BeforeTick(badLink, ForkTick + 1);
        int faultRows = badState.CaptureRows(badLink).Length;
        using var faultRunner = new DeterministicRunner<PlanarFrame>(badLink, plan(badState));
        faultRunner.BeginTick(ForkTick + 1, new PlanarFrame { Delta = Delta }, Delta);
        Require(Drain(faultRunner) == DeterministicTickStatus.Faulted &&
            faultRunner.Fault is InvalidDataException { Message: "Stale planar body observation." } &&
            faultRunner.LastCompleted is null && badState.Tick == ForkTick,
            "A stale body observation did not fault before publishing the tick.");
        faultRunner.DiscardWorld();
        int rows = frames.Sum(frame => frame.Rows) + replay.Sum(frame => frame.Rows) + falseBegins.Rows + faultRows;
        Console.WriteLine($"{lane} ticks={frames.Count} restoredTicks={replay.Count} expectedRows={rows} " +
            $"orderedImpulses={frames.Sum(frame => frame.Impulses)} beginContacts={frames.Sum(frame => frame.Began.Length)} " +
            "exactOracle=passed continuation=passed historyLossDetected=passed allocator=passed staleLinkFault=passed");
        var hashes = frames.Select(frame => $"{frame.Tick}:{Convert.ToHexString(SHA256.HashData(frame.Checkpoint))}")
            .Prepend($"0:{Convert.ToHexString(SHA256.HashData(initial))}");
        Console.WriteLine($"{lane} checkpointTrace={string.Join(',', hashes)}");
        return new(initial, frames.ToArray(), rows);
    }

    private static Frame Advance(World world, PlanarState state, DeterministicRunner<PlanarFrame> runner, int tick)
    {
        state.BeforeTick(world, tick);
        var integrated = PlanarOracle.Integrate(state.CaptureRows(world), Delta);
        var oracle = PlanarOracle.Solve(integrated);
        var began = oracle.Pairs.Except(state.History).ToArray();
        runner.BeginTick(tick, new PlanarFrame { Delta = Delta }, Delta);
        if (Drain(runner) != DeterministicTickStatus.Completed)
            throw new InvalidOperationException($"Planar runner faulted at tick {tick}.", runner.Fault);
        Require(state.CaptureRows(world).SequenceEqual(oracle.Rows) && state.History.SequenceEqual(oracle.Pairs) &&
            state.LastTickImpulseCount == oracle.Impulses, $"Planar state or pair order differs at tick {tick}.");

        var events = runner.LastCompleted!.Events;
        Require(events.Count == integrated.Length + began.Length, "Unexpected planar event count.");
        int offset = 0;
        foreach (var sample in integrated.OrderBy(row => state.GetOwner(row.Slot).Entity))
        {
            var owner = state.GetOwner(sample.Slot);
            var observation = events[offset++];
            var data = observation.Get<PlanarObservation>();
            Require(observation.Key.TickIndex == tick && observation.Key.Stage == SystemStage.Update &&
                observation.Key.SystemOrder == 0 && observation.Key.SourceKind == DeterministicSourceKind.Entity &&
                observation.Key.SourceId == owner.Entity && observation.Key.SourceGeneration == owner.EntityVersion &&
                observation.Key.LocalOrdinal == 0 &&
                sample == new PlanarSample(data.Slot, data.Generation, data.X, data.Y, data.Vx, data.Vy),
                $"Planar observation differs at tick {tick}, slot {sample.Slot}.");
        }
        for (int index = 0; index < began.Length; index++)
        {
            var start = events[offset++];
            Require(start.Key.TickIndex == tick && start.Key.Stage == SystemStage.Update &&
                start.Key.SystemOrder == 1 && start.Key.SourceKind == DeterministicSourceKind.Job &&
                start.Key.LocalOrdinal == index && start.Get<PlanarBegin>().Pair == began[index],
                $"Planar begin-contact stream differs at tick {tick}, index {index}.");
        }
        return new(tick, integrated.Length, oracle.Impulses, began, Save(world, state.Capture()));
    }

    private static DeterministicTickStatus Drain(DeterministicRunner<PlanarFrame> runner)
    {
        var timer = Stopwatch.StartNew();
        var wait = new SpinWait();
        while (true)
        {
            var status = runner.Advance(1);
            if (status != DeterministicTickStatus.Pending) return status;
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Planar runner stayed pending.");
            wait.SpinOnce();
        }
    }

    internal static void Compare(IReadOnlyList<Frame> first, IReadOnlyList<Frame> second)
    {
        Require(first.Count == second.Count, "Different continuation trace lengths.");
        for (int index = 0; index < first.Count; index++)
        {
            var a = first[index];
            var b = second[index];
            Require(a.Tick == b.Tick && a.Rows == b.Rows && a.Impulses == b.Impulses &&
                a.Began.SequenceEqual(b.Began) && a.Checkpoint.AsSpan().SequenceEqual(b.Checkpoint),
                $"Canonical continuation differs at tick {a.Tick}.");
        }
    }

    private static byte[] Save(World world, CheckpointExtension extension)
    {
        using var stream = new MemoryStream();
        WorldSerializer.WriteBinary(world, stream, Registry, new CheckpointWriteOptions { Extensions = [extension] });
        return stream.ToArray();
    }

    private static World Load(byte[] bytes, int chunk, out PlanarState state)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var world = WorldSerializer.ReadBinary(stream, Registry, out var extensions, new CheckpointReadOptions
        {
            RequireExactSchema = true, WorldOptions = new WorldOptions(chunk),
            Extensions = new Dictionary<uint, uint> { [PlanarState.ExtensionId] = PlanarState.ExtensionVersion },
            RequiredExtensions = [PlanarState.ExtensionId],
        });
        try
        {
            state = PlanarState.Restore(world, extensions.Single());
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MotionCodec : IComponentCodec<PlanarMotion>
    {
        public void Write(in PlanarMotion value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X.Raw));
            fields.Set(2, FieldValue.From(value.Y.Raw));
            fields.Set(3, FieldValue.From(value.Vx.Raw));
            fields.Set(4, FieldValue.From(value.Vy.Raw));
        }

        public PlanarMotion Read(ComponentReader fields) => new()
        {
            X = Fixed.FromRaw(fields.Get(1).AsInt64()), Y = Fixed.FromRaw(fields.Get(2).AsInt64()),
            Vx = Fixed.FromRaw(fields.Get(3).AsInt64()), Vy = Fixed.FromRaw(fields.Get(4).AsInt64()),
        };
    }

    private sealed class LinkCodec : IComponentCodec<PlanarLink>
    {
        public void Write(in PlanarLink value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.Slot));
            fields.Set(2, FieldValue.From(value.Generation));
        }

        public PlanarLink Read(ComponentReader fields) => new()
        {
            Slot = fields.Get(1).AsInt32(), Generation = fields.Get(2).AsInt32(),
        };
    }
}
