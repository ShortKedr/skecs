namespace SKECS.Tests;

public sealed class AdaptivePlacementTests
{
    private record struct Position(int X);
    private record struct Effect(int Power);
    private record struct Marker(int Tag);

    private static ComponentPlacementOptions Permission() => new ComponentPlacementOptions()
        .AllowAdaptiveStorage<Effect>().AllowAdaptiveStorage<Marker>();

    private static WorldOptions Options(int capacity, bool packed = true) => new WorldOptions(capacity)
        .WithChurnControl(new ChurnControlOptions(targetRetainedBytes: 32_768))
        .WithComponentPlacement(packed ? Permission().WithPackedPools(typeof(Effect), typeof(Marker)) : Permission());

    [Fact]
    public void PermissionIsExplicitAndLegacyConsumersRejectPackedTypes()
    {
        Assert.Throws<ArgumentException>(() => Permission().WithPackedPools(typeof(Position)));
        Assert.Throws<ArgumentException>(() => Permission().WithPackedPools(typeof(Effect), typeof(Effect)));
        var pinned = Permission().RequireArchetypeStorage<Effect>();
        Assert.False(pinned.CanUsePackedPool(typeof(Effect)));
        Assert.Throws<ArgumentException>(() => pinned.WithPackedPools(typeof(Effect)));
        Assert.Throws<ArgumentException>(() => new World(new WorldOptions().WithComponentPlacement(
            Permission().WithPackedPools(typeof(Effect)))));

        using var world = new World(Options(4));
        world.CreateEntity(new Position(1), new Effect(2));
        Assert.Throws<InvalidOperationException>(() => world.Query<Effect>());
        Assert.Throws<InvalidOperationException>(() => world.Query<Position, Effect>());
        Assert.Throws<InvalidOperationException>(() => world.Query<Position, Marker, Effect>());
        Assert.Throws<InvalidOperationException>(() => InvokeLegacyConsumer<Effect>(world));
        Assert.Throws<InvalidOperationException>(() => world.QueryBuilder().With<Effect>());
        Assert.Throws<InvalidOperationException>(() => world.QueryBuilder().Without<Marker>());
        var workload = new DeterministicWorkload<int>();
        workload.AddJob(SystemStage.Update, new DeterministicQuery().Read<Effect>(), new Noop());
        Assert.Throws<InvalidOperationException>(() => new DeterministicRunner<int>(world, workload));
        Assert.Equal(1, world.QueryBuilder().With<Position>().Build().MatchCount);
    }

    private static void InvokeLegacyConsumer<T>(World world) where T : unmanaged
        => world.Query<T>().Chunks();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(256)]
    public void PackedSwapDeleteKeepsGenerationMembershipAndAccessorCorrect(int capacity)
    {
        using var world = new World(Options(capacity));
        using var other = new World(Options(capacity));
        var first = world.CreateEntity(new Effect(1), new Marker(7));
        var second = world.CreateEntity(new Position(2), new Effect(8), new Marker(9));
        var third = world.CreateEntity(new Effect(3));
        var accessor = world.GetAccessor<Effect>();
        var foreign = other.CreateEntity(new Effect(10));
        Assert.False(accessor.Has(foreign));
        Assert.Throws<InvalidOperationException>(() => world.Add(second, new Effect(11)));
        Assert.Equal(8, accessor.Get(second).Power);
        Assert.True(world.Remove<Effect>(first));
        Assert.False(accessor.TryGet(first, out _));
        Assert.False(world.Remove<Effect>(first));
        Assert.Equal(3, accessor.Get(third).Power);
        accessor.Get(third).Power = 33;
        Assert.Equal(33, world.Get<Effect>(third).Power);
        world.DestroyEntity(second);
        var replacement = world.CreateEntity();
        Assert.Equal(second.Id, replacement.Id);
        Assert.Equal(second.Version + 1, replacement.Version);
        Assert.False(world.TryGet<Effect>(second, out _));
        Assert.False(world.Has<Marker>(replacement));
        world.Set(replacement, new Effect(4));
        Assert.False(accessor.Has(second));
        Assert.True(accessor.TryGet(replacement, out var value));
        Assert.Equal(new Effect(4), value);
        world.Dispose();
        Assert.False(accessor.Has(replacement));
        Assert.Throws<InvalidOperationException>(() => accessor.Get(replacement));
    }

    [Fact]
    public void AdaptiveBuilderCountsCandidatesAndTracksTheSmallestLivePool()
    {
        using var world = new World(Options(2));
        var a = world.CreateEntity(new Position(1), new Effect(10), new Marker(100));
        var b = world.CreateEntity(new Position(2), new Effect(20));
        var c = world.CreateEntity(new Position(3), new Marker(300));
        var d = world.CreateEntity(new Position(4), new Effect(40));
        var pooled = world.AdaptiveQueryBuilder().With<Effect>().With<Marker>().Build();
        var mixed = world.AdaptiveQueryBuilder().With<Position>().With<Effect>().Without<Marker>().Build();
        Assert.Equal(2, pooled.CandidateCount);
        Assert.Equal(1, pooled.EntityCount);
        Assert.Equal(4, mixed.CandidateCount);
        Assert.Equal(2, mixed.EntityCount);
        var found = new List<Entity>();
        mixed.ForEach(found.Add);
        Assert.Equal(new[] { b.Id, d.Id }, found.Select(entity => entity.Id).Order());

        world.Remove<Effect>(b);
        world.Remove<Effect>(d);
        Assert.Equal(1, pooled.CandidateCount);
        Assert.Equal(0, mixed.EntityCount);
        world.Add(c, new Effect(30));
        var processor = new Combine();
        world.QueryAdaptive<Position, Effect, Marker>().ForEach(ref processor);
        Assert.Equal(2, processor.Count);
        Assert.Equal(111, world.Get<Effect>(a).Power);
        Assert.Equal(333, world.Get<Effect>(c).Power);
        Assert.Equal(0, world.AdaptiveQueryBuilder().With<Effect>().Without<Effect>().Build().EntityCount);

        Assert.Throws<InvalidOperationException>(() => pooled.ForEach(_ => world.CreateEntity()));
        world.CreateEntity();
    }

    [Fact]
    public void PackedAndArchetypePagesUseTheSameStartupAndReuseBackings()
    {
        var options = new WorldOptions(1)
            .WithChurnControl(new ChurnControlOptions(65_536, startupReserveBytes: 39,
                startupBlocks: [new ChurnStartupBlock(31, 1)], maxOversizePercent: 0))
            .WithComponentPlacement(Permission().WithPackedPools(typeof(Effect)));
        var diagnostics = new DiagnosticBuffer(64);
        using var world = new World(options, diagnostics);
        var entity = world.CreateEntity(new Effect(6));
        world.Remove<Effect>(entity);
        var archetypeEntity = world.CreateEntity(new Position(13));
        Assert.Equal(13, world.Get<Position>(archetypeEntity).X);
        world.Set(entity, new Effect(55));
        world.Dispose();

        var events = Enumerable.Range(0, diagnostics.Count).Select(index => diagnostics[index]).ToArray();
        var reserved = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.PooledChunkAllocated));
        var reusedPacked = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.PackedPageReused));
        var pooled = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.PackedPageReturned));
        var reusedArchetype = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.ChunkReused));
        Assert.Equal(reserved.BackingId, reusedPacked.BackingId);
        Assert.Equal(reserved.BackingId, pooled.BackingId);
        Assert.Equal(reserved.BackingId, reusedArchetype.BackingId);
        var allocatedPacked = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.PackedPageAllocated));
        var releasedPacked = Assert.Single(events.Where(item => item.Kind == DiagnosticEventKind.PackedPageReleased));
        Assert.Equal(allocatedPacked.BackingId, releasedPacked.BackingId);
        Assert.Equal(0, diagnostics.DroppedEvents);
    }

    [Fact]
    public void CheckpointBudgetRefusesAFirstPackedPageWithoutAddingMembership()
    {
        using var world = new World(Options(4));
        var entity = world.CreateEntity();
        var budget = 0L;
        Assert.False(world.TryAddCheckpointComponent(entity, new Effect(8), ref budget));
        Assert.Equal(0L, budget);
        Assert.False(world.Has<Effect>(entity));
        budget = 16_384;
        Assert.True(world.TryAddCheckpointComponent(entity, new Effect(8), ref budget));
        Assert.InRange(budget, 1, 16_383);
        Assert.Equal(8, world.Get<Effect>(entity).Power);
    }

    [Theory]
    [InlineData(19, 1)]
    [InlineData(193, 16)]
    [InlineData(769, 256)]
    public void IndependentlyRunWorldsMatchCompleteStateAndEventsAtEveryTick(int seed, int capacity)
    {
        using var baseline = new World(Options(capacity, false));
        using var candidate = new World(Options(capacity));
        var baselineEntities = Initialize(baseline);
        var candidateEntities = Initialize(candidate);
        var baselineRandom = new Random(seed);
        var candidateRandom = new Random(seed);
        Assert.Equal(Capture(baseline), Capture(candidate));
        for (var tick = 0; tick < 100; tick++)
        {
            var expectedEvents = Tick(baseline, baselineEntities, baselineRandom, tick);
            var actualEvents = Tick(candidate, candidateEntities, candidateRandom, tick);
            Assert.Equal(expectedEvents, actualEvents);
            Assert.Equal(Capture(baseline), Capture(candidate));
        }
    }

    private static Entity[] Initialize(World world)
    {
        var factory = world.PrepareEntity<Position, Effect>();
        var entities = new Entity[40];
        factory.CreateBatch(entities, new Position(1), new Effect(2));
        return entities;
    }

    private static (int Id, int Version, int Power)[] Tick(World world, Entity[] entities, Random random, int tick)
    {
        for (var operation = 0; operation < 12; operation++)
        {
            ref var entity = ref entities[random.Next(entities.Length)];
            switch (random.Next(5))
            {
                case 0: world.Remove<Effect>(entity); break;
                case 1: world.Set(entity, new Marker(tick)); break;
                case 2: world.Remove<Marker>(entity); break;
                case 3: world.Set(entity, new Effect(tick)); break;
                default:
                    world.DestroyEntity(entity);
                    entity = world.CreateEntity(new Position(tick));
                    break;
            }
        }

        var events = new List<(int Id, int Version, int Power)>();
        var processor = new Simulate(events);
        world.QueryAdaptive<Position, Effect>().ForEach(ref processor);
        // Significant events are committed by the scenario in canonical entity order.
        return events.OrderBy(item => item.Id).ToArray();
    }

    private static object[] Capture(World world)
    {
        var values = new List<object>();
        var view = world.OpenCheckpoint();
        var free = new int[view.FreeCount];
        view.CopyFreeIds(free);
        values.Add(free.Length);
        foreach (var id in free) values.Add(id);
        for (var id = 0; id < view.SlotCount; id++)
        {
            var slot = view.GetSlot(id);
            values.Add((id, slot.Version, slot.Alive));
            if (!slot.Alive) continue;
            var entity = world.BindEntity(id, slot.Version);
            var types = new Type[view.GetComponentCount(entity)];
            for (var index = 0; index < types.Length; index++) types[index] = view.GetComponentType(entity, index);
            foreach (var type in types.OrderBy(type => type.Name)) values.Add(type);
            if (view.TryGet<Position>(entity, out var position)) values.Add(position);
            if (view.TryGet<Effect>(entity, out var effect)) values.Add(effect);
            if (view.TryGet<Marker>(entity, out var marker)) values.Add(marker);
        }
        return values.ToArray();
    }

    private readonly struct Noop : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job) { }
    }

    private struct Combine : IQueryProcessor<Position, Effect, Marker>
    {
        public int Count;
        public void Execute(Entity entity, ref Position position, ref Effect effect, ref Marker marker)
        {
            effect.Power += position.X + marker.Tag;
            Count++;
        }
    }

    private readonly struct Simulate(List<(int, int, int)> events) : IQueryProcessor<Position, Effect>
    {
        public void Execute(Entity entity, ref Position position, ref Effect effect)
        {
            effect.Power += position.X;
            events.Add((entity.Id, entity.Version, effect.Power));
        }
    }
}
