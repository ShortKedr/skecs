#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

// This host is deliberately small. Update and Poll own the runner; Render only sees published values.
internal sealed class HostSession : IDisposable
{
    internal const int RowCount = 131;
    private readonly Func<DeterministicWorkload<HostInput>> _workload;
    private readonly int _chunkCapacity;
    private readonly int _workerCount;
    private World _world = null!;
    private DeterministicRunner<HostInput> _runner = null!;
    private Entity[] _bodies = Array.Empty<Entity>();
    private Frame _published = null!;

    internal HostSession(Func<DeterministicWorkload<HostInput>> workload, int chunkCapacity, int workerCount)
    {
        _workload = workload;
        _chunkCapacity = chunkCapacity;
        _workerCount = workerCount;
    }

    internal bool Paused { get; set; }
    internal Exception? Fault => _runner.Fault;
    internal int Captures { get; private set; }
    internal Frame Render() => Volatile.Read(ref _published);

    internal void Reset()
    {
        int generation = _published is null ? 0 : _published.Generation + 1;
        Dispose();
        _world = new World(new WorldOptions(_chunkCapacity));
        _bodies = new Entity[RowCount];
        for (int index = 0; index < _bodies.Length; index++)
            _bodies[index] = _world.CreateEntity(new HostBody { Index = index, Position = Fixed.FromRaw(17L * index - 39) });
        _runner = new DeterministicRunner<HostInput>(_world, _workload(), workerCount: _workerCount);
        Paused = false;
        Publish(generation, 0, Array.Empty<RenderEvent>());
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

internal sealed class Frame
{
    internal Frame(int generation, long tick, IReadOnlyList<long> positions, IReadOnlyList<RenderEvent> events)
    {
        Generation = generation;
        Tick = tick;
        Positions = positions;
        Events = events;
    }

    internal int Generation { get; }
    internal long Tick { get; }
    internal IReadOnlyList<long> Positions { get; }
    internal IReadOnlyList<RenderEvent> Events { get; }
}

internal readonly struct RenderEvent
{
    internal RenderEvent(DeterministicOrderKey key, int value)
    {
        Key = key;
        Value = value;
    }

    internal DeterministicOrderKey Key { get; }
    internal int Value { get; }
}

internal sealed class AuditedBatches : IDeterministicBatchProcessor<HostInput>
{
    private readonly GaBatchProcessor<HostInput> _processor;

    internal AuditedBatches(GaBatchProcessor<HostInput> processor) => _processor = processor;

    // Hold the host's observation of the real GPU submission to make pause/reset deterministic on fast devices.
    internal bool Hold { get; set; }
    internal int Submitted { get; private set; }
    internal int InFlight { get; private set; }
    internal int Discarded { get; private set; }

    public IDeterministicBatchSubmission<HostInput> Begin(DeterministicBatch<HostInput> batch)
    {
        var submission = _processor.Begin(batch);
        Submitted++;
        InFlight++;
        return new Submission(this, submission);
    }

    private sealed class Submission : IDeterministicBatchSubmission<HostInput>
    {
        private readonly AuditedBatches _owner;
        private readonly IDeterministicBatchSubmission<HostInput> _inner;

        internal Submission(AuditedBatches owner, IDeterministicBatchSubmission<HostInput> inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public bool IsCompleted => !_owner.Hold && _inner.IsCompleted;
        public void Apply(DeterministicBatch<HostInput> batch) => _inner.Apply(batch);

        public void Discard()
        {
            _inner.Discard();
            _owner.Discarded++;
        }

        public void Dispose()
        {
            _inner.Dispose();
            _owner.InFlight--;
        }
    }
}
