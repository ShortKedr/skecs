using System.Diagnostics;
using System.Numerics;
using SKECS;
using SKECS.Numerics;
using SKECS.Serialization;

internal static class ContinuationChecks
{
    internal const int ForkTick = 4;
    private const int FinalTick = 12;
    private const long Scale = 1L << 32;
    private const long Delta = Scale / 8;
    private static readonly CodecRegistry Registry = new CodecRegistry()
        .Register(new ComponentSchema(301, 1, new FieldSchema(1, FieldKind.Int64), new FieldSchema(2, FieldKind.Int64)),
            new MotionCodec())
        .Register(new ComponentSchema(302, 1, new FieldSchema(1, FieldKind.Int32), new FieldSchema(2, FieldKind.Int32)),
            new LinkCodec());

    internal readonly record struct Frame(long Tick, byte[] Checkpoint, ContactPair[] Began, int Rows, int Impulses);
    private readonly record struct ExpectedBody(int Owner, int OwnerGeneration, int Slot, int Generation, long Position, long InitialVelocity);
    private readonly record struct OracleContact(ContactPair Pair, long Difference);

    internal static Frame[] Run(Func<ContactService, DeterministicWorkload<IntegrationFrame>> createPlan, string lane, int chunk)
    {
        using var original = new World(new WorldOptions(chunk));
        var service = ContactService.Seed(original);
        var initial = Save(original, service.Capture());
        using (var decoded = Load(initial, chunk, out var initialService))
            Require(initial.SequenceEqual(Save(decoded, initialService.Capture())), "Initial checkpoint changed after restore.");

        using var originalRunner = NewRunner(original, service, createPlan);
        var prefix = Advance(original, service, originalRunner, [], ForkTick);
        var start = prefix[^1];
        Require(service.Previous.Count >= 2 && service.Free.SequenceEqual(new[] { 3, 4 }) &&
            service.PendingLifecycle == 4 && prefix[0].Impulses >= 2, "Fork missed significant service state.");
        byte[] noHistory = Save(original, service.Capture(includeHistory: false));
        byte[] withoutExtension = Save(original);
        ExpectInvalid(() => Load(withoutExtension, chunk, out _));
        TestInvalidLink(start.Checkpoint, chunk);

        var tail = Advance(original, service, originalRunner, service.Previous.ToArray(), FinalTick);
        using var restored = Load(start.Checkpoint, chunk, out var recovered);
        Require(start.Checkpoint.SequenceEqual(Save(restored, recovered.Capture())), "Checkpoint changed after restore.");
        Require(recovered.Free.SequenceEqual(new[] { 3, 4 }) && recovered.Previous.Count >= 2 &&
            recovered.RandomState != 0 && recovered.PendingLifecycle == 4, "Restore omitted significant service state.");
        using var restoredRunner = NewRunner(restored, recovered, createPlan);
        var replay = Advance(restored, recovered, restoredRunner, recovered.Previous.ToArray(), FinalTick);
        Compare(tail, replay, "uninterrupted and restored execution");
        Require(recovered.Slots[3].Generation == 2 && recovered.Slots[4].Generation == 2 &&
            recovered.Slots[2].Generation == 2 && recovered.PendingLifecycle == 0 &&
            recovered.RandomState != 0x8d4c2a31u, "Continuation missed allocator reuse, RNG or future requests.");

        using var broken = Load(noHistory, chunk, out var noPrevious);
        using var brokenRunner = NewRunner(broken, noPrevious, createPlan);
        var incorrect = Advance(broken, noPrevious, brokenRunner, [], ForkTick + 1).Single();
        Require(incorrect.Began.Except(tail[0].Began).Any(),
            "Losing the previous contact history did not change the next begin-contact stream.");

        var all = prefix.Concat(tail).ToArray();
        VerifyFaultRecovery(original, service, originalRunner, createPlan, chunk, all[^1]);
        Console.WriteLine($"{lane} ticks={all.Length} restoredTicks={replay.Length} beginContacts={all.Sum(frame => frame.Began.Length)} " +
            $"orderedImpulses={all.Sum(frame => frame.Impulses)} initialRestore=passed continuation=passed " +
            "historyLossDetected=passed invalidLinkRejected=passed missingExtensionRejected=passed " +
            "faultCheckpointRecovery=passed allocator=passed exactOracle=passed");
        return all;
    }

    private static DeterministicRunner<IntegrationFrame> NewRunner(World world, ContactService service,
        Func<ContactService, DeterministicWorkload<IntegrationFrame>> createPlan) => new(world, createPlan(service), workerCount: 2);

    private static Frame[] Advance(World world, ContactService service, DeterministicRunner<IntegrationFrame> runner,
        ContactPair[] previous, long last)
    {
        var result = new List<Frame>();
        for (long tick = service.CompletedTick + 1; tick <= last; tick++)
        {
            service.BeforeTick(world, tick);
            var bodies = new List<ExpectedBody>();
            for (int slot = 0; slot < service.Slots.Count; slot++)
            {
                var body = service.Slots[slot];
                if (!body.Alive)
                    continue;
                var motion = world.Get<ContactMotion>(world.BindEntity(body.Owner, body.OwnerGeneration));
                long position = checked(motion.Position.Raw + IntegrationDelta(motion.Velocity.Raw));
                bodies.Add(new ExpectedBody(body.Owner, body.OwnerGeneration, slot, body.Generation, position, motion.Velocity.Raw));
            }

            var contacts = FindContacts(bodies);
            var begins = contacts.Select(item => item.Pair).Except(previous).ToArray();
            var velocities = bodies.ToDictionary(body => body.Slot, body => body.InitialVelocity);
            int impulses = 0;
            foreach (var contact in contacts)
            {
                int first = contact.Pair.First;
                int second = contact.Pair.Second;
                long left = velocities[first];
                long right = velocities[second];
                int direction = contact.Difference < 0 ? -1 : 1;
                if (left.CompareTo(right) == direction)
                {
                    velocities[first] = right;
                    velocities[second] = left;
                    impulses++;
                }
            }

            BeginIntegration(runner, tick);
            var status = Drain(runner);
            if (status != DeterministicTickStatus.Completed)
                throw new InvalidOperationException("Service fixture runner faulted.", runner.Fault);
            var events = runner.LastCompleted!.Events;
            Require(events.Count == bodies.Count + begins.Length && runner.LastCompleted.TickIndex == tick,
                $"Unexpected event stream at tick {tick}.");
            int index = 0;
            foreach (var body in bodies.OrderBy(body => body.Owner))
            {
                var observingEvent = events[index++];
                var sample = observingEvent.Get<BodyObservation>();
                var key = observingEvent.Key;
                Require(key.TickIndex == tick && key.Stage == SystemStage.Update && key.SystemOrder == 0 &&
                    key.SourceKind == DeterministicSourceKind.Entity &&
                    key.SourceId == body.Owner && key.SourceGeneration == body.OwnerGeneration && key.LocalOrdinal == 0 &&
                    sample.Slot == body.Slot && sample.Generation == body.Generation && sample.Position.Raw == body.Position &&
                    sample.Velocity.Raw == body.InitialVelocity, $"Integrated sample differs at tick {tick}, body {body.Slot}.");
                var entity = world.BindEntity(body.Owner, body.OwnerGeneration);
                var motion = world.Get<ContactMotion>(entity);
                Require(motion.Position.Raw == body.Position && motion.Velocity.Raw == velocities[body.Slot],
                    $"Ordered service result differs at tick {tick}, body {body.Slot}.");
            }

            for (int contact = 0; contact < begins.Length; contact++)
            {
                var published = events[index++];
                Require(published.Key.TickIndex == tick && published.Key.Stage == SystemStage.Update &&
                    published.Key.SystemOrder == 1 && published.Key.SourceKind == DeterministicSourceKind.Job &&
                    published.Key.LocalOrdinal == contact && published.Get<ContactStarted>().Pair == begins[contact],
                    $"Begin-contact stream differs at tick {tick}, index {contact}.");
            }

            previous = contacts.Select(item => item.Pair).ToArray();
            Require(service.Previous.SequenceEqual(previous) && service.Impulses == impulses, $"Service history differs at tick {tick}.");
            service.Validate(world);
            result.Add(new Frame(tick, Save(world, service.Capture()), begins, bodies.Count, impulses));
        }
        return result.ToArray();
    }

    private static OracleContact[] FindContacts(List<ExpectedBody> bodies)
    {
        var pairs = from left in bodies
                    from right in bodies
                    where left.Slot < right.Slot
                    let difference = (BigInteger)right.Position - left.Position
                    where BigInteger.Abs(difference) <= 2 * Scale
                    orderby left.Slot, right.Slot
                    select new OracleContact(new ContactPair(left.Slot, left.Generation, right.Slot, right.Generation), (long)difference);
        return pairs.ToArray();
    }

    private static long IntegrationDelta(long velocity)
    {
        var quotient = BigInteger.DivRem((BigInteger)velocity * Delta, Scale, out var remainder);
        var half = BigInteger.Abs(remainder) * 2;
        if (half > Scale || half == Scale && !quotient.IsEven)
            quotient += remainder.Sign;
        return (long)quotient;
    }

    internal static void Compare(IReadOnlyList<Frame> first, IReadOnlyList<Frame> second, string label)
    {
        Require(first.Count == second.Count, $"{label} frame counts differ.");
        for (int index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[index];
            Require(left.Tick == right.Tick && left.Rows == right.Rows && left.Impulses == right.Impulses &&
                left.Began.SequenceEqual(right.Began) && left.Checkpoint.AsSpan().SequenceEqual(right.Checkpoint),
                $"{label} first differ at tick {left.Tick}.");
        }
    }

    private static void BeginIntegration(DeterministicRunner<IntegrationFrame> runner, long tick) =>
        runner.BeginTick(tick, new IntegrationFrame { Delta = Fixed.FromRaw(Delta) }, Fixed.FromRaw(Delta));

    private static DeterministicTickStatus Drain(DeterministicRunner<IntegrationFrame> runner)
    {
        var clock = Stopwatch.StartNew();
        var wait = new SpinWait();
        while (runner.Advance(1) == DeterministicTickStatus.Pending)
        {
            if (clock.Elapsed.TotalSeconds > 20)
                throw new TimeoutException("Service fixture runner did not complete.");
            wait.SpinOnce(sleep1Threshold: -1);
        }
        return runner.Status;
    }

    private static void VerifyFaultRecovery(World world, ContactService service, DeterministicRunner<IntegrationFrame> runner,
        Func<ContactService, DeterministicWorkload<IntegrationFrame>> createPlan, int chunk, Frame frame)
    {
        var owner = service.Slots[0];
        world.Get<PhysicsLink>(world.BindEntity(owner.Owner, owner.OwnerGeneration)).Generation++;
        long next = frame.Tick + 1;
        var last = runner.LastCompleted;
        service.BeforeTick(world, next);
        BeginIntegration(runner, next);
        Require(Drain(runner) == DeterministicTickStatus.Faulted && runner.Fault is InvalidDataException &&
            ReferenceEquals(runner.LastCompleted, last), "Service fault published a new completed result.");
        bool captureRejected = false;
        try { world.OpenCheckpoint(); }
        catch (InvalidOperationException) { captureRejected = true; }
        Require(captureRejected, "A faulted world was open for capture.");
        runner.DiscardWorld();

        using var recovered = Load(frame.Checkpoint, chunk, out var restored);
        using var resumedRunner = NewRunner(recovered, restored, createPlan);
        var result = Advance(recovered, restored, resumedRunner, restored.Previous.ToArray(), next).Single();
        Require(result.Tick == next, "Recovery did not complete the retry.");
    }

    private static byte[] Save(World world, params CheckpointExtension[] extensions)
    {
        using var stream = new MemoryStream();
        WorldSerializer.WriteBinary(world, stream, Registry, new CheckpointWriteOptions { Extensions = extensions });
        return stream.ToArray();
    }

    private static World Load(byte[] bytes, int chunk, out ContactService service)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var world = WorldSerializer.ReadBinary(stream, Registry, out var extensions, new CheckpointReadOptions
        {
            RequireExactSchema = true,
            WorldOptions = new WorldOptions(chunk),
            Extensions = new Dictionary<uint, uint> { [ContactService.ExtensionId] = ContactService.ExtensionVersion },
            RequiredExtensions = new[] { ContactService.ExtensionId }
        });
        try
        {
            service = ContactService.Restore(world, extensions.Single());
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static void TestInvalidLink(byte[] checkpoint, int chunk)
    {
        using var world = Load(checkpoint, chunk, out var service);
        var owner = service.Slots[0];
        var entity = world.BindEntity(owner.Owner, owner.OwnerGeneration);
        world.Get<PhysicsLink>(entity).Generation++;
        byte[] broken = Save(world, service.Capture());
        ExpectInvalid(() => Load(broken, chunk, out _));
    }

    private static void ExpectInvalid(Func<World> load)
    {
        try { using var _ = load(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("A corrupted checkpoint was accepted.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class MotionCodec : IComponentCodec<ContactMotion>
    {
        public void Write(in ContactMotion value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.Position.Raw));
            fields.Set(2, FieldValue.From(value.Velocity.Raw));
        }

        public ContactMotion Read(ComponentReader fields) => new()
        {
            Position = Fixed.FromRaw(fields.Get(1).AsInt64()), Velocity = Fixed.FromRaw(fields.Get(2).AsInt64())
        };
    }

    private sealed class LinkCodec : IComponentCodec<PhysicsLink>
    {
        public void Write(in PhysicsLink value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.Slot));
            fields.Set(2, FieldValue.From(value.Generation));
        }

        public PhysicsLink Read(ComponentReader fields) => new()
        {
            Slot = fields.Get(1).AsInt32(), Generation = fields.Get(2).AsInt32()
        };
    }
}
