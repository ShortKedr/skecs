using BenchmarkDotNet.Attributes;
using SKECS.Numerics;

namespace SKECS.Benchmarks.Runner;

[MemoryDiagnoser]
public class DeterministicRunnerBenchmarks
{
    private World _world = null!;
    private DeterministicRunner<int> _runner = null!;
    private long _tick;

    [Params(1, 63, 256)]
    public int ChunkCapacity { get; set; }

    [Params(1, 3, int.MaxValue)]
    public int Budget { get; set; }

    [Params(false, true)]
    public bool BufferedChanges { get; set; }

    public long NativeStorageBytes => _world.GetStorageStatistics().NativeBytes;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(new WorldOptions(chunkCapacity: ChunkCapacity));
        for (var i = 0; i < 8192; i++)
        {
            var entity = _world.CreateEntity(new Counter(i));
            if (i % 2 == 0) _world.Add(entity, new Split());
        }

        var systems = new DeterministicWorkload<int>().AddSystem(SystemStage.Update,
            new DeterministicQuery().With<Counter>().ReadWrite<Counter>().Write<Tag>(), new Update(BufferedChanges));
        _runner = new DeterministicRunner<int>(_world, systems);
        _tick = 0;
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
    private readonly record struct Split;
    private readonly record struct Tag;

    private readonly struct Update(bool bufferedChanges) : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            for (var i = 0; i < job.Count; i++)
            {
                var value = unchecked(job.Read<Counter>(i).Value + job.Input);
                job.Write(i, new Counter(value));
                var entity = job.GetEntity(i);
                if (!bufferedChanges || entity.Id % 8 != 0) continue;
                var source = job.ForEntity(i);
                source.Emit(value);
                // Change archetype on every tick to measure actual migration during the barrier.
                if (job.TickIndex % 2 == 0)
                    source.Add(entity, new Tag());
                else
                    source.Remove<Tag>(entity);
            }
        }
    }
}
