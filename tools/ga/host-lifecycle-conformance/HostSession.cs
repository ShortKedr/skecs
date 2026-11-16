using SKECS;
using SKECS.GA;
using SKECS.Numerics;

// This host is deliberately small. Update and Poll own the runner; Render only sees published values.
internal sealed class HostSession(
    Func<DeterministicWorkload<HostInput>> workload, int chunkCapacity) : IDisposable
{
    internal const int RowCount = 131;
    private World _world = null!;
    private DeterministicRunner<HostInput> _runner = null!;
    private Entity[] _bodies = [];
    private Frame _published = null!;

    internal bool Paused { get; set; }
    internal Exception? Fault => _runner.Fault;
    internal int Captures { get; private set; }
    internal Frame Render() => Volatile.Read(ref _published);

    internal void Reset()
    {
        int generation = _published is null ? 0 : _published.Generation + 1;
        Dispose();
        _world = new World(new WorldOptions(chunkCapacity));
        _bodies = new Entity[RowCount];
        for (int index = 0; index < _bodies.Length; index++)
            _bodies[index] = _world.CreateEntity(new HostBody { Index = index, Position = Fixed.FromRaw(17L * index - 39) });
        _runner = new DeterministicRunner<HostInput>(_world, workload(), workerCount: OperatingSystem.IsBrowser() ? 1 : 2);
        Paused = false;
        Publish(generation, 0, []);
    }

    internal DeterministicTickStatus Update(in HostInput input)
    {
        if (Paused || _runner.Status == DeterministicTickStatus.Faulted)
            return _runner.Status;
        if (_runner.Status != DeterministicTickStatus.Pending)
            _runner.BeginTick(Render().Tick + 1, in input, Fixed.One);
        return Poll();
    }

    internal DeterministicTickStatus Poll()
    {
        if (Paused || _runner.Status != DeterministicTickStatus.Pending)
            return _runner.Status;
        var status = _runner.Advance(1);
        if (status == DeterministicTickStatus.Completed)
        {
            var result = _runner.LastCompleted!;
            var events = new RenderEvent[result.Events.Count];
            for (int index = 0; index < events.Length; index++)
            {
                var item = result.Events[index];
                events[index] = new RenderEvent(item.Key, item.Get<int>());
            }
            Publish(Render().Generation, result.TickIndex, events);
        }
        return status;
    }

    private void Publish(int generation, long tick, RenderEvent[] events)
    {
        var positions = new long[_bodies.Length];
        for (int index = 0; index < positions.Length; index++)
            positions[index] = _world.Get<HostBody>(_bodies[index]).Position.Raw;
        Volatile.Write(ref _published, new Frame(generation, tick, Array.AsReadOnly(positions), Array.AsReadOnly(events)));
        Captures++;
    }

    // Only the test asks for a direct accessor while Pending, to verify that the runner rejects it.
    internal void ReadLiveWorld() => _ = _world.Get<HostBody>(_bodies[0]);

    public void Dispose()
    {
        _runner?.Dispose();
        _world?.Dispose();
    }
}

internal sealed record Frame(int Generation, long Tick, IReadOnlyList<long> Positions, IReadOnlyList<RenderEvent> Events);
internal readonly record struct RenderEvent(DeterministicOrderKey Key, int Value);

internal sealed class AuditedBatches(GaBatchProcessor<HostInput> processor) : IDeterministicBatchProcessor<HostInput>
{
    // Hold the host's observation of the real GPU submission to make pause/reset deterministic on fast devices.
    internal bool Hold { get; set; }
    internal int Submitted { get; private set; }
    internal int InFlight { get; private set; }
    internal int Discarded { get; private set; }

    public IDeterministicBatchSubmission<HostInput> Begin(DeterministicBatch<HostInput> batch)
    {
        var submission = processor.Begin(batch);
        Submitted++;
        InFlight++;
        return new Submission(this, submission);
    }

    private sealed class Submission(AuditedBatches owner, IDeterministicBatchSubmission<HostInput> inner)
        : IDeterministicBatchSubmission<HostInput>
    {
        public bool IsCompleted => !owner.Hold && inner.IsCompleted;
        public void Apply(DeterministicBatch<HostInput> batch) => inner.Apply(batch);

        public void Discard()
        {
            inner.Discard();
            owner.Discarded++;
        }

        public void Dispose()
        {
            inner.Dispose();
            owner.InFlight--;
        }
    }
}
