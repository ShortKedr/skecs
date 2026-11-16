using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class BatchRunnerTests
{
    [Fact]
    public void EmptyBatchStageDoesNotSubmitAndLeavesBudgetForNextSystem()
    {
        using var world = new World();
        var processor = new ManualProcessor(world, []);
        var workload = Workload(processor);
        workload.AddJob(SystemStage.Update, new DeterministicQuery(), new EmptyStageObserver());
        using var runner = new DeterministicRunner<TickInput>(world, workload);

        runner.BeginTick(41, new TickInput { Value = 10 }, Fixed.FromRaw(17));
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.Equal(0, processor.BeginCount);
        Assert.Equal(10, Assert.Single(runner.LastCompleted!.Events).Get<int>());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(256)]
    public void PollsAndAppliesCanonicalBatchesUsingOnlyNewJobBudget(int capacity)
    {
        using var world = new World(new WorldOptions(chunkCapacity: capacity));
        var entities = Populate(world, 261);
        var processor = new ManualProcessor(world, entities);
        var workload = Workload(processor);
        var observerQuery = new DeterministicQuery().With<Published>().Read<Published>();
        workload.AddSystem(SystemStage.Update, observerQuery, new Observer());
        using var runner = new DeterministicRunner<TickInput>(world, workload, workerCount: 4);
        var input = new TickInput { Value = 10 };
        runner.BeginTick(41, in input, Fixed.FromRaw(17));
        input.Value = 999;
        Require(runner.Advance(2) == DeterministicTickStatus.Pending, "First batch must wait on its fence.");
        Require(processor.Submissions.Count == 1, "First advance must submit one batch.");
        CheckRange(processor.Submissions[0], 0, 2, 64, 64);

        Require(runner.Advance(18) == DeterministicTickStatus.Pending &&
            runner.Advance(1) == DeterministicTickStatus.Pending, "Unsignalled polling must stay pending.");
        Require(processor.Submissions.Count == 1 && processor.Submissions[0].ApplyCount == 0 &&
            runner.LastCompleted == null, "Polling consumed work or published results.");
        Assert.Throws<InvalidOperationException>(() => world.Set(entities[0], new Position(-99)));

        processor.Submissions[0].Ready = true;
        Require(runner.Advance(2) == DeterministicTickStatus.Pending, "Second batch must wait.");
        Require(processor.Submissions.Count == 2, "Applying the last batch spent the new-job budget.");
        CheckRange(processor.Submissions[1], 2, 2, 64, 64);
        CheckApplied(processor.Submissions[0]);

        processor.Submissions[1].Ready = true;
        Require(runner.Advance(1) == DeterministicTickStatus.Pending, "Tail batch must wait.");
        CheckRange(processor.Submissions[2], 4, 1, 5);
        CheckApplied(processor.Submissions[1]);
        Require(runner.LastCompleted == null, "A partial barrier published output.");

        processor.Submissions[2].Ready = true;
        Require(runner.Advance(1) == DeterministicTickStatus.Pending,
            "The newly visible observer must receive only one newly available job.");
        CheckApplied(processor.Submissions[2]);
        Require(runner.LastCompleted == null, "A partially run next system published output.");
        Require(runner.Advance(4) == DeterministicTickStatus.Completed, "The final four CPU jobs must finish.");
        Require(runner.Advance(10) == DeterministicTickStatus.Completed && processor.Submissions.Count == 3,
            "Completed runner resubmitted work.");

        var events = runner.LastCompleted!.Events;
        Require(events.Count == entities.Length * 3 + 5, "Events were lost, duplicated or committed early.");
        for (int i = 0; i < entities.Length; i++)
        {
            Require(world.Get<Position>(entities[i]).Value == i + 10, "Staged writes used the wrong row or mutable input.");
            Require(world.Get<Published>(entities[i]).Value == i, "A deferred component missed the normal barrier.");
            var first = events[2 * i];
            var second = events[2 * i + 1];
            Require(first.Get<int>() == i && second.Get<int>() == -i &&
                first.Key.SourceKind == DeterministicSourceKind.Entity && first.Key.TickIndex == 41 &&
                first.Key.SystemOrder == 0 && first.Key.SourceId == entities[i].Id &&
                first.Key.SourceGeneration == entities[i].Version && first.Key.LocalOrdinal == 0 &&
                second.Key.LocalOrdinal == 2, "Entity events did not retain canonical keys.");
            var observation = events[2 * entities.Length + 5 + i];
            Require(observation.Key.SystemOrder == 1 && observation.Get<int>() == i,
                "The CPU observer did not see all structurally committed rows.");
        }
        for (int job = 0; job < 5; job++)
        {
            var evt = events[2 * entities.Length + job];
            Require(evt.Key.SourceKind == DeterministicSourceKind.Job && evt.Key.SourceId == job &&
                evt.Get<int>() == 10000 + job, "Canonical job outputs were lost or reordered.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDrainsBeforeWorldLeaseIsReleased(bool dispose)
    {
        using var world = new World();
        var entities = Populate(world, 3);
        var processor = new ManualProcessor(world, entities);
        using var runner = new DeterministicRunner<TickInput>(world, Workload(processor));
        runner.BeginTick(41, new TickInput { Value = 10 }, Fixed.FromRaw(17));
        Require(runner.Advance(1) == DeterministicTickStatus.Pending, "Expected a pending submission.");
        var submission = processor.Submissions.Single();
        if (dispose)
        {
            runner.Dispose();
            Assert.Throws<ObjectDisposedException>(() => world.Get<Position>(entities[0]));
            Assert.Throws<ObjectDisposedException>(() => runner.Advance(1));
        }
        else
        {
            runner.Abort();
            Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entities[0]));
            Require(runner.Advance(1) == DeterministicTickStatus.Faulted, "Abort left work eligible for retry.");
        }
        Require(runner.Status == DeterministicTickStatus.Faulted && runner.Fault is OperationCanceledException &&
            runner.LastCompleted == null && processor.BeginCount == 1 && submission.ApplyCount == 0 &&
            submission.DiscardCount == 1 && submission.DisposeCount == 1,
            "Abort/dispose did not discard exactly once while preserving its cancellation.");
    }

    [Theory]
    [InlineData(Failure.Begin)]
    [InlineData(Failure.Poll)]
    [InlineData(Failure.Apply)]
    [InlineData(Failure.Dispose)]
    public void FailedSubmissionRetainsPrimaryAndIsNeverRetried(Failure when)
    {
        using var world = new World();
        var entities = Populate(world, 100);
        var primary = new ArgumentException("primary");
        var discard = new ApplicationException("discard");
        var release = new ApplicationException("release");
        var processor = new ManualProcessor(world, entities)
        {
            FailAt = when,
            Primary = primary,
            DiscardFault = when is Failure.Poll or Failure.Apply ? discard : null,
            DisposeFault = when is Failure.Poll or Failure.Apply ? release : null
        };
        using var runner = new DeterministicRunner<TickInput>(world, Workload(processor));
        runner.BeginTick(41, new TickInput { Value = 10 }, Fixed.FromRaw(17));
        var initial = runner.Advance(1);
        if (when is Failure.Apply or Failure.Dispose)
        {
            Require(initial == DeterministicTickStatus.Pending, "Fixture expected a later asynchronous fault.");
            processor.Submissions.Single().Ready = true;
            runner.Advance(4);
        }
        Require(runner.Advance(100) == DeterministicTickStatus.Faulted && runner.LastCompleted == null &&
            processor.BeginCount == 1, "Failure allowed publishing or resubmitting a tick.");
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entities[0]));
        Assert.Throws<InvalidOperationException>(() => runner.BeginTick(42, default, Fixed.One));
        var error = runner.Fault!;
        if (when is Failure.Poll or Failure.Apply)
        {
            var disposeAggregate = error as AggregateException;
            Require(disposeAggregate != null && ReferenceEquals(disposeAggregate.InnerExceptions[1], release) &&
                disposeAggregate.InnerExceptions[0] is AggregateException drainAggregate &&
                ReferenceEquals(drainAggregate.InnerExceptions[0], primary) &&
                ReferenceEquals(drainAggregate.InnerExceptions[1], discard), "Cleanup errors replaced the primary failure.");
            var submission = processor.Submissions.Single();
            Require(submission.ApplyCount == (when == Failure.Apply ? 1 : 0) && submission.DiscardCount == 1 &&
                submission.DisposeCount == 1, "A failed poll/apply did not drain and release exactly once.");
        }
        else
        {
            Require(ReferenceEquals(error, primary), "The primary failure identity changed.");
            if (when == Failure.Dispose)
            {
                var submission = processor.Submissions.Single();
                Require(submission.ApplyCount == 1 && submission.DisposeCount == 1 && submission.DiscardCount == 0,
                    "Completed work was discarded or disposed twice after release failed.");
            }
        }
    }

    private static Entity[] Populate(World world, int length)
    {
        var entities = new Entity[length];
        for (int i = 0; i < length; i++)
        {
            var entity = world.CreateEntity(new Position(i), new Undeclared(i));
            if (i % 2 == 0) world.Add(entity, new Alternate());
            entities[i] = entity;
        }
        return entities;
    }

    private static DeterministicWorkload<TickInput> Workload(ManualProcessor processor)
    {
        var query = new DeterministicQuery().With<Position>().ReadWrite<Position>().Write<Published>();
        return new DeterministicWorkload<TickInput>().AddBatchSystem(SystemStage.Update, query, processor);
    }

    private static void CheckRange(ManualSubmission submission, int first, int count, params int[] rows)
    {
        Require(submission.First == first && submission.Count == count && submission.RowSizes.SequenceEqual(rows),
            "Canonical batch range or row boundaries changed.");
    }

    private static void CheckApplied(ManualSubmission submission)
    {
        Require(submission.ApplyCount == 1 && submission.DiscardCount == 0 && submission.DisposeCount == 1,
            "A completed batch was applied or released more than once.");
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    public enum Failure { None, Begin, Poll, Apply, Dispose }
    private struct TickInput { public int Value; }
    private readonly record struct Position(int Value);
    private readonly record struct Published(int Value);
    private readonly record struct Undeclared(int Value);
    private readonly struct Alternate;

    private readonly struct Observer : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            for (int row = 0; row < job.Count; row++)
                job.ForEntity(row).Emit(job.Read<Published>(row).Value);
        }
    }

    private readonly struct EmptyStageObserver : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job) => job.ForJob().Emit(job.Input.Value);
    }

    private sealed class ManualProcessor(World world, Entity[] entities) : IDeterministicBatchProcessor<TickInput>
    {
        public List<ManualSubmission> Submissions { get; } = [];
        public int BeginCount { get; private set; }
        public Failure FailAt { get; init; }
        public Exception? Primary { get; init; }
        public Exception? DiscardFault { get; init; }
        public Exception? DisposeFault { get; init; }

        public IDeterministicBatchSubmission<TickInput> Begin(DeterministicBatch<TickInput> batch)
        {
            BeginCount++;
            if (FailAt == Failure.Begin) throw Primary!;
            ValidateTick(batch);
            var rows = new List<int>();
            var values = new List<int>();
            bool blockedLow = false, blockedHigh = false;
            try { _ = batch.GetReadJob(batch.FirstJobId - 1); }
            catch (ArgumentOutOfRangeException) { blockedLow = true; }
            try { _ = batch.GetReadJob(batch.FirstJobId + batch.JobCount); }
            catch (ArgumentOutOfRangeException) { blockedHigh = true; }
            if (!blockedLow || !blockedHigh) throw new InvalidOperationException("Batch offered another range's jobs.");
            for (int offset = 0; offset < batch.JobCount; offset++)
            {
                var id = batch.FirstJobId + offset;
                var job = batch.GetReadJob(id);
                if (job.JobId != id || job.TickIndex != batch.TickIndex || job.Input.Value != batch.Input.Value || job.Step != batch.Step)
                    throw new InvalidOperationException("Job did not inherit its batch metadata.");
                bool writeJobBlocked = false, readBlocked = false;
                try { _ = batch.GetJob(id); }
                catch (InvalidOperationException) { writeJobBlocked = true; }
                try { _ = job.Read<Undeclared>(0); }
                catch (InvalidOperationException) { readBlocked = true; }
                if (!writeJobBlocked || !readBlocked)
                    throw new InvalidOperationException("Begin exposed a write-capable job or an undeclared read.");
                rows.Add(job.Count);
                for (int row = 0; row < job.Count; row++)
                {
                    int ordinal = id * DeterministicRunner<TickInput>.LogicalJobSize + row;
                    int value = job.Read<Position>(row).Value;
                    if (value != ordinal || job.GetEntity(row) != entities[ordinal] ||
                        !job.TryRead<Position>(row, out var position) || position.Value != value)
                        throw new InvalidOperationException("Canonical row IDs were derived from physical chunk layout.");
                    values.Add(value);
                }
            }
            CheckWorldStillOwned();
            var submission = new ManualSubmission(this, batch.FirstJobId, batch.JobCount, rows.ToArray(), values.ToArray());
            Submissions.Add(submission);
            return submission;
        }

        public static void ValidateTick(DeterministicBatch<TickInput> batch)
        {
            if (batch.TickIndex != 41 || batch.Step.Raw != 17 || batch.Input.Value != 10 ||
                batch.SystemOrder != 0 || batch.Stage != SystemStage.Update)
                throw new InvalidOperationException("Frozen inputs or stage metadata changed.");
        }

        public void CheckWorldStillOwned()
        {
            try { _ = world.Get<Position>(entities[0]); }
            catch (InvalidOperationException error) when (error is not ObjectDisposedException) { return; }
            throw new InvalidOperationException("World lease was available or already disposed during submission and drain.");
        }
    }

    private sealed class ManualSubmission(ManualProcessor owner, int first, int count, int[] rowSizes, int[] values)
        : IDeterministicBatchSubmission<TickInput>
    {
        public int First { get; } = first;
        public int Count { get; } = count;
        public int[] RowSizes { get; } = rowSizes;
        public bool Ready { get; set; }
        public int ApplyCount { get; private set; }
        public int DiscardCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool IsCompleted => owner.FailAt == Failure.Poll ? throw owner.Primary! : Ready;

        public void Apply(DeterministicBatch<TickInput> batch)
        {
            ApplyCount++;
            if (!Ready || batch.FirstJobId != First || batch.JobCount != Count)
                throw new InvalidOperationException("Apply used a different range or ran before the manual fence.");
            ManualProcessor.ValidateTick(batch);
            owner.CheckWorldStillOwned();
            if (owner.FailAt == Failure.Apply) throw owner.Primary!;
            int captured = values.Length;
            for (int offset = batch.JobCount - 1; offset >= 0; offset--)
            {
                var job = batch.GetJob(batch.FirstJobId + offset);
                job.ForJob().Emit(10000 + job.JobId);
                for (int row = job.Count - 1; row >= 0; row--)
                {
                    int value = values[--captured];
                    if (job.Read<Position>(row).Value != value)
                        throw new InvalidOperationException("Apply received different rows than Begin.");
                    job.Write(row, new Position(value + job.Input.Value));
                    var source = job.ForEntity(row);
                    source.Emit(value);
                    source.Add(job.GetEntity(row), new Published(value));
                    source.Emit(-value);
                }
            }
        }

        public void Discard()
        {
            DiscardCount++;
            owner.CheckWorldStillOwned();
            if (owner.DiscardFault != null) throw owner.DiscardFault;
        }

        public void Dispose()
        {
            DisposeCount++;
            owner.CheckWorldStillOwned();
            if (owner.FailAt == Failure.Dispose) throw owner.Primary!;
            if (owner.DisposeFault != null) throw owner.DisposeFault;
        }
    }
}
