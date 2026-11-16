using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicOrderedApplyTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Receipt(int Value);
    private readonly record struct Request(int Value);
    private readonly record struct OtherRequest(int Value);
    private readonly record struct Absent;

    [Fact]
    public void AppliesSortedGroupEventsOnTheOwnerThreadBeforeTheNextSystem()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 1));
        var entity = world.CreateEntity(new Position(0));
        var threadId = Environment.CurrentManagedThreadId;
        var step = Fixed.FromRatio(1, 5);
        int applied = 0;
        var workload = new DeterministicWorkload<byte>()
            .AddJob(SystemStage.PreUpdate, new DeterministicQuery(), new EmitJob(-1))
            .AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("test-fold", 1), group =>
            {
                group.AddJob(new DeterministicQuery().Write<Position>(), new ProduceAndSet(entity));
                group.AddJob(new EmitRequests());
                group.AddOrderedApply(new DeterministicQuery().ReadWrite<Position>().Write<Receipt>(), context =>
                {
                    applied++;
                    Assert.Equal(threadId, Environment.CurrentManagedThreadId);
                    Assert.Equal(12L, context.TickIndex);
                    Assert.Equal(SystemStage.Update, context.Stage);
                    Assert.Equal(2, context.SystemOrder);
                    Assert.Equal((byte)3, context.Input);
                    Assert.Equal(step, context.Step);
                    Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
                    Assert.Equal(new[] { new Request(5), new Request(7), new Request(13) },
                        context.Events.ToArray().Select(item => item.Get<Request>()));
                    Assert.Equal(new[] { 1, 1, 0 },
                        context.Events.ToArray().Select(item => item.Key.ProducerOrder));
                    Assert.Equal(new[] { 0, 1, 1 },
                        context.Events.ToArray().Select(item => item.Key.LocalOrdinal));
                    Assert.All(context.Events.ToArray(), item =>
                    {
                        Assert.Equal(1, item.Key.SystemOrder);
                        Assert.Equal(DeterministicSourceKind.Job, item.Key.SourceKind);
                        Assert.Equal(0, item.Key.SourceId);
                    });
                    Assert.True(context.TryRead<Position>(entity.Id, entity.Version, out var before));
                    Assert.Equal(new Position(10), before);
                    foreach (var item in context.Events)
                    {
                        Assert.True(context.TryRead<Position>(entity.Id, entity.Version, out var prior));
                        var next = new Position(prior.Value * 10 + item.Get<Request>().Value);
                        Assert.True(context.TryWrite(entity.Id, entity.Version, next));
                    }
                    Assert.True(context.TryRead<Position>(entity.Id, entity.Version, out var final));
                    Assert.Equal(new Position(10583), final);
                    var ticket = context.Commands.Create();
                    context.Commands.Add(ticket, new Receipt(final.Value));
                    context.Commands.Emit(new Receipt(final.Value));
                });
            })
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<Receipt>().Read<Receipt>(),
                new ObserveReceipts());
        using var runner = new DeterministicRunner<byte>(world, workload, workerCount: 2);
        byte input = 3;
        runner.BeginTick(12, input, step);
        input = 99;

        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(3));
        Assert.Equal(1, applied);
        Assert.Null(runner.LastCompleted);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(1, applied);

        var events = runner.LastCompleted!.Events;
        Assert.Equal(6, events.Count);
        Assert.Equal(-1, events[0].Get<int>());
        Assert.Equal(new[] { 5, 7, 13 }, events.Skip(1).Take(3).Select(item => item.Get<Request>().Value));
        Assert.Equal(new Receipt(10583), events[4].Get<Receipt>());
        Assert.Equal(2, events[4].Key.SystemOrder);
        Assert.Equal(0, events[4].Key.ProducerOrder);
        Assert.Equal(DeterministicSourceKind.Job, events[4].Key.SourceKind);
        Assert.Equal(2, events[4].Key.LocalOrdinal);
        Assert.Null(events[4].Key.DomainKey);
        Assert.Equal(new Receipt(10583), events[5].Get<Receipt>());
        Assert.Equal(3, events[5].Key.SystemOrder);
        Assert.Equal(DeterministicSourceKind.Entity, events[5].Key.SourceKind);
        Assert.Equal(new Position(10583), world.Get<Position>(entity));
    }

    [Fact]
    public void ReceivesOnlyItsGroupEventsAndRunsWithAnEmptyEventSpan()
    {
        using var world = new World();
        var observed = new List<(long Tick, int System, int Count)>();
        var workload = new DeterministicWorkload<byte>()
            .AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddJob(new ConditionalFirst());
                group.AddOrderedApply(new DeterministicQuery(), context =>
                {
                    observed.Add((context.TickIndex, context.SystemOrder, context.Events.Length));
                    foreach (var item in context.Events)
                    {
                        Assert.Equal(new Request(42), item.Get<Request>());
                        Assert.Equal(context.TickIndex, item.Key.TickIndex);
                    }
                    context.Commands.Emit(new Receipt(context.Events.Length));
                });
            })
            .AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddSystem(new DeterministicQuery().With<Absent>(), new UnexpectedRows());
                group.AddJob(new ConditionalSecond());
                group.AddOrderedApply(new DeterministicQuery(), context =>
                {
                    observed.Add((context.TickIndex, context.SystemOrder, context.Events.Length));
                    foreach (var item in context.Events)
                    {
                        Assert.Equal(new OtherRequest(99), item.Get<OtherRequest>());
                        Assert.Equal(context.TickIndex, item.Key.TickIndex);
                    }
                });
            });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(1, 0, Fixed.One);
        Assert.True(runner.Advance(2) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        runner.BeginTick(3, 1, Fixed.One);
        Assert.True(runner.Advance(2) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(new (long, int, int)[] { (1, 1, 1), (1, 3, 0), (3, 1, 0), (3, 3, 1) }, observed);
        Assert.Equal(new Receipt(0), runner.LastCompleted!.Events[0].Get<Receipt>());
        Assert.Equal(new OtherRequest(99), runner.LastCompleted.Events[1].Get<OtherRequest>());
    }

    [Fact]
    public void WaitsForPendingProducerBeforeOrderedApply()
    {
        using var world = new World();
        world.CreateEntity(new Position(0));
        var producer = new DelayedProducer();
        int calls = 0;
        var workload = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddBatchSystem(new DeterministicQuery().With<Position>(), producer);
            group.AddOrderedApply(new DeterministicQuery(), context =>
            {
                calls++;
                Assert.Equal(new Request(42), Assert.Single(context.Events.ToArray()).Get<Request>());
                context.Commands.Emit(new Receipt(5));
            });
        });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(0, calls);
        Assert.Null(runner.LastCompleted);
        producer.IsCompleted = true;
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(1, calls);
        Assert.Equal(2, runner.LastCompleted!.Events.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsAndWritesOnlyTheCurrentGenerationAndExistingComponents(bool packed)
    {
        var placement = new ComponentPlacementOptions().AllowAdaptiveStorage<Position>().AllowAdaptiveStorage<Absent>();
        if (packed)
            placement = placement.WithPackedPools(typeof(Position), typeof(Absent));
        var options = new WorldOptions(1).WithChurnControl(new ChurnControlOptions(65_536))
            .WithComponentPlacement(placement);
        using var world = new World(options);
        var stale = world.CreateEntity(new Position(0));
        world.DestroyEntity(stale);
        var entity = world.CreateEntity(new Position(11));
        Assert.Equal(stale.Id, entity.Id);
        Assert.NotEqual(stale.Version, entity.Version);
        var removed = world.CreateEntity(new Absent());

        var workload = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(new DeterministicQuery().ReadWrite<Position>().ReadWrite<Absent>(), context =>
            {
                Assert.False(context.TryRead<Position>(stale.Id, stale.Version, out var missing));
                Assert.Equal(default, missing);
                Assert.False(context.TryWrite(stale.Id, stale.Version, new Position(88)));
                Assert.False(context.TryRead<Position>(-1, entity.Version, out _));
                Assert.False(context.TryWrite(-1, entity.Version, new Position(88)));
                Assert.False(context.TryRead<Absent>(entity.Id, entity.Version, out _));
                Assert.False(context.TryWrite(entity.Id, entity.Version, new Absent()));
                Assert.True(context.TryRead<Position>(entity.Id, entity.Version, out var current));
                Assert.Equal(new Position(11), current);
                Assert.True(context.TryWrite(entity.Id, entity.Version, new Position(22)));
                context.Commands.Add(entity, new Position(99));
                context.Commands.Add(entity, new Absent());
                context.Commands.Remove<Absent>(removed);
            });
        });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(new Position(22), world.Get<Position>(entity));
        Assert.True(world.Has<Absent>(entity));
        Assert.False(world.Has<Absent>(removed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OppositeAccessDoesNotAuthorizeReadOrWrite(bool write)
    {
        using var world = new World();
        var entity = world.CreateEntity(new Position(11));
        var access = write ? new DeterministicQuery().Read<Position>() : new DeterministicQuery().Write<Position>();
        var workload = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(access, context =>
            {
                if (write)
                    context.TryWrite(entity.Id, entity.Version, new Position(22));
                else
                    context.TryRead<Position>(entity.Id, entity.Version, out _);
            });
        });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Contains(write ? "Write access" : "Read access", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    [Fact]
    public void FailedCallbackKeepsThePreviousTickAndStopsDownstreamSystems()
    {
        using var world = new World();
        var entity = world.CreateEntity(new Position(1));
        var failure = new InvalidOperationException("Ordered apply fault.");
        int downstream = 0;
        var workload = new DeterministicWorkload<byte>()
            .AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddJob(new ConditionalFirst());
                group.AddOrderedApply(new DeterministicQuery().Write<Position>(), context =>
                {
                    context.Commands.Emit(new Receipt(10 + context.Input));
                    Assert.True(context.TryWrite(entity.Id, entity.Version, new Position(2 + context.Input)));
                    if (context.Input != 0)
                        throw failure;
                });
            })
            .AddSnapshotPreparation(SystemStage.PostUpdate, new DeterministicQuery().With<Position>(),
                _ => downstream++);
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var completed = runner.LastCompleted!;
        Assert.Equal(new Receipt(10), completed.Events[1].Get<Receipt>());

        runner.BeginTick(1, 1, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Same(failure, runner.Fault);
        Assert.Same(completed, runner.LastCompleted);
        Assert.Equal(new Receipt(10), completed.Events[1].Get<Receipt>());
        Assert.Equal(1, downstream);
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrderedApplyAccessCannotContainQueryFilters(bool include)
    {
        var workload = new DeterministicWorkload<byte>();
        var access = include ? new DeterministicQuery().With<Position>() : new DeterministicQuery().Without<Position>();
        Assert.Throws<ArgumentException>(() => workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(access, _ => { });
        }));
    }

    [Fact]
    public void OrderedApplyAccessCannotDeclareEntitySourceOrder()
    {
        var workload = new DeterministicWorkload<byte>();
        var sort = DeterministicSourceOrder.By<Position>("test-order", 1,
            static (in Position position) => new DeterministicDomainKey(position.Value, 0));
        var access = new DeterministicQuery().Read<Position>().OrderBy(sort);
        Assert.Throws<ArgumentException>(() => workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(access, _ => { });
        }));
    }

    [Fact]
    public void OrderedApplyMustFollowTheProducersAndBeRegisteredOnlyOnce()
    {
        var workload = new DeterministicWorkload<byte>();
        Assert.Throws<InvalidOperationException>(() => workload.AddCommitGroup(SystemStage.Update,
            group => group.AddOrderedApply(new DeterministicQuery(), _ => { })));
        Assert.Throws<InvalidOperationException>(() => workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(new DeterministicQuery(), _ => { });
            group.AddJob(new EmitJob(1));
        }));
        Assert.Throws<InvalidOperationException>(() => workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new EmitJob(0));
            group.AddOrderedApply(new DeterministicQuery(), _ => { });
            group.AddOrderedApply(new DeterministicQuery(), _ => { });
        }));
        using var world = new World();
        workload.AddJob(SystemStage.Update, new DeterministicQuery(), new EmitJob(99));
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var single = Assert.Single(runner.LastCompleted!.Events);
        Assert.Equal(99, single.Get<int>());
        Assert.Equal(0, single.Key.SystemOrder);
    }

    private readonly struct EmitJob(int value) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job) => job.ForJob().Emit(value);
    }

    private readonly struct ProduceAndSet(Entity entity) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob();
            source.WithDomainOrder(new DeterministicDomainKey(1, 0)).Set(entity, new Position(10));
            source.WithDomainOrder(new DeterministicDomainKey(3, 0)).Emit(new Request(13));
        }
    }

    private readonly struct EmitRequests : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob().WithDomainOrder(new DeterministicDomainKey(-2, 0));
            source.Emit(new Request(5));
            source.Emit(new Request(7));
        }
    }

    private readonly struct ObserveReceipts : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            Assert.Equal(1, job.Count);
            job.ForEntity(0).Emit(job.Read<Receipt>(0));
        }
    }

    private readonly struct ConditionalFirst : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            if (job.Input == 0)
                job.ForJob().Emit(new Request(42));
        }
    }

    private readonly struct ConditionalSecond : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            if (job.Input == 1)
                job.ForJob().Emit(new OtherRequest(99));
        }
    }

    private readonly struct UnexpectedRows : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job) => throw new InvalidOperationException("An empty query ran a job.");
    }

    private sealed class DelayedProducer : IDeterministicBatchProcessor<byte>, IDeterministicBatchSubmission<byte>
    {
        public bool IsCompleted { get; set; }
        public IDeterministicBatchSubmission<byte> Begin(DeterministicBatch<byte> batch) => this;
        public void Apply(DeterministicBatch<byte> batch) => batch.GetJob(0).ForEntity(0).Emit(new Request(42));
        public void Discard() { }
        public void Dispose() { }
    }
}
