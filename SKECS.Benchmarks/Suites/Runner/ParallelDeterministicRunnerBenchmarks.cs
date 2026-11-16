using BenchmarkDotNet.Attributes;
using SKECS.Numerics;

namespace SKECS.Benchmarks.Runner;

[MemoryDiagnoser]
public class ParallelDeterministicRunnerBenchmarks
{
    public enum WorkloadKind
    {
        EmptyJobs,
        ComponentWrites,
        BufferedMigration,
    }

    private World _world = null!;
    private DeterministicRunner<int> _runner = null!;
    private long _tick;

    [Params(192, 8192)]
    public int RowCount { get; set; }

    [Params(3, 64, int.MaxValue)]
    public int Budget { get; set; }

    [ParamsAllValues]
    public WorkloadKind Workload { get; set; }

    [ParamsSource(nameof(WorkerCounts))]
    public int WorkerCount { get; set; }

    public IEnumerable<int> WorkerCounts => new[] { 1, 2, 4, Environment.ProcessorCount }.Distinct();
    public int ExpectedEvents => Workload == WorkloadKind.BufferedMigration ? RowCount / 8 : 0;
    public long NativeStorageBytes => _world.GetStorageStatistics().NativeBytes;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(new WorldOptions(chunkCapacity: 256));
        for (var i = 0; i < RowCount; i++)
            _world.CreateEntity(new Counter(i), new Increment(1 + i % 7));

        var workload = new DeterministicWorkload<int>();
        var query = new DeterministicQuery().With<Counter>().With<Increment>();
        if (Workload == WorkloadKind.EmptyJobs)
            workload.AddSystem(SystemStage.Update, query, new EmptyJob());
        else
            workload.AddSystem(SystemStage.Update, query.ReadWrite<Counter>().Read<Increment>().Write<Tag>(),
                new Update(Workload == WorkloadKind.BufferedMigration));
        _runner = new DeterministicRunner<int>(_world, workload, WorkerCount);
        _tick = 0;

        var entity = _world.BindEntity(0, 1);
        if (Tick() != ExpectedEvents || _world.Get<Counter>(entity).Value != (Workload == WorkloadKind.EmptyJobs ? 0 : 2)
            || _world.Has<Tag>(entity) != (Workload == WorkloadKind.BufferedMigration))
            throw new InvalidOperationException("The parallel benchmark produced unexpected output.");
    }

    [Benchmark]
    public int Tick()
    {
        _runner.BeginTick(_tick++, 1, Fixed.One);
        DeterministicTickStatus status;
        do
        {
            status = _runner.Advance(Budget);
        } while (status == DeterministicTickStatus.Pending);

        if (status != DeterministicTickStatus.Completed)
            throw new InvalidOperationException("The benchmark tick faulted.", _runner.Fault);
        return _runner.LastCompleted!.Events.Count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _runner.Dispose();
        _world.Dispose();
    }

    private readonly record struct Counter(int Value);
    private readonly record struct Increment(int Value);
    private readonly record struct Tag;

    private readonly struct EmptyJob : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job) { }
    }

    private readonly struct Update(bool bufferedChanges) : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            for (var row = 0; row < job.Count; row++)
            {
                var value = unchecked(job.Read<Counter>(row).Value + job.Read<Increment>(row).Value + job.Input);
                job.Write(row, new Counter(value));
                if (!bufferedChanges) continue;

                var entity = job.GetEntity(row);
                if (entity.Id % 8 != 0) continue;
                var source = job.ForEntity(row);
                source.Emit(value);
                if (job.TickIndex % 2 == 0)
                    source.Add(entity, new Tag());
                else
                    source.Remove<Tag>(entity);
            }
        }
    }
}
