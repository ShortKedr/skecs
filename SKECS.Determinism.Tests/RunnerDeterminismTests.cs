using SKECS.Numerics;

namespace SKECS.Determinism;

public sealed class RunnerDeterminismTests
{
    private const int EntityCount = 133;
    private const int JobCount = 3;
    private const int ReductionSeed = 97;

    public static TheoryData<int, int> StorageAndBudgets()
    {
        var cases = new TheoryData<int, int>();
        foreach (var capacity in new[] { 1, 3, 257 })
            foreach (var budget in new[] { 1, 2, int.MaxValue })
                cases.Add(capacity, budget);
        return cases;
    }

    [Theory]
    [MemberData(nameof(StorageAndBudgets))]
    public void CompletedTicksMatchIntegerReferenceAcrossStorageAndJobBudgets(int capacity, int budget)
    {
        using var world = new World(new WorldOptions(capacity));
        var entities = new Entity[EntityCount];
        for (var id = 0; id < entities.Length; id++)
            entities[id] = world.CreateEntity(StartingPosition(id), Velocity(id));
        var first = entities[7];
        var second = entities[83];
        world.DestroyEntity(first);
        world.DestroyEntity(second);
        entities[83] = world.CreateEntity(StartingPosition(83), Velocity(83));
        entities[7] = world.CreateEntity(StartingPosition(7), Velocity(7));
        for (var id = 0; id < entities.Length; id += 3)
            world.Add(entities[id], new Marker(37, id));

        var workload = new DeterministicWorkload<int>();
        var reduction = workload.AddReduction(ReductionSeed, static (total, leaf) => unchecked(31 * total + leaf));
        workload.AddSystem(SystemStage.Update,
            new DeterministicQuery().With<Transform>().With<Motion>().ReadWrite<Transform>().Read<Motion>(),
            new Move(reduction));
        using var runner = new DeterministicRunner<int>(world, workload);

        int[] inputs = [-3, 0, 7, 11];
        var inputSum = 0L;
        for (var tick = 0; tick < inputs.Length; tick++)
        {
            inputSum += inputs[tick];
            runner.BeginTick(tick, inputs[tick], Fixed.One);
            var calls = budget >= JobCount ? 1 : (JobCount + budget - 1) / budget;
            for (var call = 1; call <= calls; call++)
            {
                var expectedStatus = call == calls ? DeterministicTickStatus.Completed : DeterministicTickStatus.Pending;
                var status = runner.Advance(budget);
                Assert.True(status == expectedStatus,
                    $"Tick {tick}, advance {call}, expected {expectedStatus}, actual {status}. {runner.Fault}");
            }

            var result = Assert.IsType<DeterministicTickResult>(runner.LastCompleted);
            Assert.Equal(tick, result.TickIndex);
            Assert.Equal(2 * EntityCount + JobCount, result.Events.Count);
            Assert.False(world.IsAlive(first));
            Assert.False(world.IsAlive(second));
            Assert.Equal(EntityCount, world.GetStorageStatistics().EntityCount);
            var expectedReduction = ReductionSeed;
            for (var id = 0; id < entities.Length; id++)
            {
                var entity = entities[id];
                var generation = id is 7 or 83 ? 2 : 1;
                Assert.Equal(id, entity.Id);
                Assert.Equal(generation, entity.Version);
                Assert.True(world.IsAlive(entity));
                var initial = StartingPosition(id);
                var motion = Velocity(id);
                var position = new Transform(unchecked((int)(initial.X + motion.Dx * inputSum)),
                    unchecked((int)(initial.Y + (long)motion.Dy * (tick + 1))));
                Assert.Equal(position, world.Get<Transform>(entity));
                Assert.Equal(motion, world.Get<Motion>(entity));
                expectedReduction = unchecked(31 * expectedReduction + position.X);

                var positionEvent = result.Events[2 * id];
                var yEvent = result.Events[2 * id + 1];
                AssertKey(positionEvent.Key, tick, DeterministicSourceKind.Entity, id, generation, ordinal: 0);
                AssertKey(yEvent.Key, tick, DeterministicSourceKind.Entity, id, generation, ordinal: 2);
                Assert.Equal(position, positionEvent.Get<Transform>());
                Assert.Equal(position.Y, yEvent.Get<int>());
            }
            Assert.Equal(expectedReduction, result.GetReduction(reduction));
            for (var job = 0; job < JobCount; job++)
            {
                var jobEvent = result.Events[2 * EntityCount + job];
                AssertKey(jobEvent.Key, tick, DeterministicSourceKind.Job, job, generation: 0, ordinal: 0);
                Assert.Equal(job == 2 ? 5 : 64, jobEvent.Get<int>());
            }
        }
    }

    private static Transform StartingPosition(int id) => new(int.MaxValue - id, int.MinValue + 3 * id);

    private static Motion Velocity(int id) => new(3 - 11 * id, -7 - id);

    private static void AssertKey(DeterministicOrderKey key, long tick, DeterministicSourceKind kind,
        int id, int generation, int ordinal) =>
        Assert.Equal((tick, SystemStage.Update, 0, kind, id, generation, ordinal),
            (key.TickIndex, key.Stage, key.SystemOrder, key.SourceKind, key.SourceId, key.SourceGeneration, key.LocalOrdinal));

    private readonly record struct Move(DeterministicReduction<int> Reduction) : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            job.ForJob().Emit(job.Count);
            // Emit each job's output in reverse row order to exercise canonical merging.
            for (var row = job.Count - 1; row >= 0; row--)
            {
                var position = job.Read<Transform>(row);
                var motion = job.Read<Motion>(row);
                var updated = new Transform(unchecked(position.X + motion.Dx * job.Input),
                    unchecked(position.Y + motion.Dy));
                job.Write(row, updated);
                var source = job.ForEntity(row);
                source.Emit(updated);
                source.Contribute(Reduction, updated.X);
                source.Emit(updated.Y);
            }
        }
    }
}
