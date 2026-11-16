using System.Runtime.InteropServices;
using SKECS;
using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class ChurnControlTests
{
    [Fact]
    public void WorldOptionsKeepLegacyDefaultsAndPreserveExistingSettings()
    {
        Assert.Null(new WorldOptions().ChurnControl);
        using var legacy = new World();
        Assert.Equal(256, legacy.ChunkCapacity);

        var control = new ChurnControlOptions(100, initialEntityCapacity: 8);
        var worldOptions = new WorldOptions(32, diagnosticsEnabled: true).WithChurnControl(control);
        Assert.Equal(32, worldOptions.ChunkCapacity);
        Assert.True(worldOptions.DiagnosticsEnabled);
        Assert.Same(control, worldOptions.ChurnControl);
        Assert.Equal(256, default(WorldOptions).WithChurnControl(control).ChunkCapacity);
    }

    [Fact]
    public void ChurnOptionsValidatePolicyInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurnControlOptions(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurnControlOptions(0, startupReserveBytes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurnControlOptions(0, initialEntityCapacity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurnControlOptions(0, maxOversizePercent: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChurnControlOptions(0, releaseBatchSize: 0));
        Assert.Throws<ArgumentException>(() => new ChurnControlOptions(0, 128));
        Assert.Throws<ArgumentException>(() => new ChurnControlOptions(0, 128, new ChurnStartupBlock[1]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(256)]
    public void LegacyRetainsDisjointEqualSizePeaks(int capacity)
    {
        using var world = new World(new WorldOptions(capacity));
        var a = Fill(world, 2 * capacity, i => new A { Value = i });
        var retained = world.GetStorageStatistics().NativeBytes;
        Empty(world, a);
        var b = Fill(world, 2 * capacity, i => new B { Value = i });
        Assert.Equal(retained * 2, world.GetStorageStatistics().NativeBytes);
        for (var i = 0; i < b.Length; i++)
        {
            Assert.Equal(a[^(i + 1)].Id, b[i].Id);
            Assert.Equal(a[^(i + 1)].Version + 1, b[i].Version);
            Assert.Equal(i, world.Get<B>(b[i]).Value);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(256)]
    public unsafe void SeparatedPeaksReuseNativeAndPinnedMemoryAcrossLayouts(int capacity)
    {
        using var world = Controlled(capacity);
        var accessor = world.GetAccessor<A>();
        var query = world.Query<A>();
        var a = Fill(world, 2 * capacity, i => new A { Value = i + 1000 });
        var aAddresses = new HashSet<(nint Column, nint Ids)>();
        foreach (var chunk in query.Chunks())
            aAddresses.Add(((nint)chunk.Component1, (nint)chunk.Entities));
        var filled = world.GetChurnControlStatistics();
        var storage = world.GetStorageStatistics();
        Assert.Equal(2, filled.AllocationCount);
        Assert.Equal(2 * capacity * 12L, filled.LivePayloadBytes);
        Assert.Equal(storage.NativeBytes + 2 * capacity * 8L, filled.OccupiedChunkCapacityBytes);

        Empty(world, a);
        var empty = world.GetChurnControlStatistics();
        Assert.Equal(0, empty.OccupiedChunkCapacityBytes);
        Assert.Equal(filled.OccupiedChunkCapacityBytes, empty.ReusableEmptyBytes);
        Assert.Equal(0, empty.ReleaseCount);
        Assert.False(query.Chunks().GetEnumerator().MoveNext());
        Assert.False(accessor.Has(a[0]));
        Assert.Equal(storage.NativeBytes, world.GetStorageStatistics().NativeBytes);

        var b = Fill(world, 2 * capacity, i => new B { Value = -i });
        foreach (var chunk in world.Query<B>().Chunks())
            Assert.True(aAddresses.Remove(((nint)chunk.Component1, (nint)chunk.Entities)));
        Assert.Empty(aAddresses);
        var reused = world.GetChurnControlStatistics();
        Assert.Equal(2, reused.AllocationCount);
        Assert.Equal(2, reused.ReuseCount);
        Assert.Equal(storage.NativeBytes, world.GetStorageStatistics().NativeBytes);
        for (var i = 0; i < b.Length; i++)
        {
            Assert.Equal(a[^(i + 1)].Id, b[i].Id);
            Assert.Equal(a[^(i + 1)].Version + 1, b[i].Version);
            Assert.Equal(-i, world.Get<B>(b[i]).Value);
            Assert.False(accessor.Has(b[i]));
        }
    }

    [Fact]
    public unsafe void LocalReserveWinsAndSimultaneousLayoutsKeepLiveAddresses()
    {
        using var world = Controlled(1);
        var a = world.CreateEntity(new A { Value = 7 });
        var b = world.CreateEntity(new B { Value = 8 });
        var aAddress = Address<A>(world);
        var bAddress = Address<B>(world);
        Assert.NotEqual(aAddress, bAddress);
        Assert.Equal(2, world.GetChurnControlStatistics().AllocationCount);
        Assert.Equal(0, world.GetChurnControlStatistics().ReuseCount);
        world.DestroyEntity(a);
        Assert.Equal(bAddress, Address<B>(world));
        world.DestroyEntity(b);
        var reused = world.CreateEntity(new A { Value = 9 });
        Assert.Equal(aAddress, Address<A>(world));
        Assert.Equal(9, world.Get<A>(reused).Value);
        Assert.Equal(1, world.GetChurnControlStatistics().ReuseCount);
    }

    [Theory]
    [InlineData(25, 3, 0)]
    [InlineData(100, 2, 1)]
    public void SizeMatchingRejectsTooSmallAndExcessivelyLargeBuffers(int oversize, long allocations, long reuses)
    {
        using var world = new World(new WorldOptions(32).WithChurnControl(
            new ChurnControlOptions(long.MaxValue, maxOversizePercent: oversize)));
        world.DestroyEntity(world.CreateEntity(new Wide { Value = 1 }));
        world.DestroyEntity(world.CreateEntity(123L));
        var a = world.CreateEntity(new A { Value = 2 });
        Assert.Equal(2, world.Get<A>(a).Value);
        Assert.Equal(allocations, world.GetChurnControlStatistics().AllocationCount);
        Assert.Equal(reuses, world.GetChurnControlStatistics().ReuseCount);
        var replacementWide = world.CreateEntity(new Wide { Value = 3 });
        Assert.Equal(3, world.Get<Wide>(replacementWide).Value);
        Assert.Equal(allocations, world.GetChurnControlStatistics().AllocationCount);
    }

    [Fact]
    public void StartupReserveIsIndependentAndMaintenanceReleasesOneBlockAtATime()
    {
        const int native = 31;
        const int pinned = 8;
        var blocks = new[] { new ChurnStartupBlock(native, 4) };
        var options = new ChurnControlOptions(0, 4 * (native + pinned), blocks, initialEntityCapacity: 16,
            releaseBatchSize: 1);
        blocks[0] = default;
        using var world = new World(new WorldOptions(1).WithChurnControl(options));
        var initial = world.GetChurnControlStatistics();
        Assert.Equal(4, initial.AllocationCount);
        Assert.Equal(4 * (native + pinned), initial.ReusableEmptyBytes);
        Assert.Equal(4 * native, world.GetStorageStatistics().NativeBytes);
        Assert.True(initial.NonReclaimableBytes > 0);
        Assert.Equal(1, world.TrimChurnMemory());
        world.RunAll();
        Assert.Equal(2, world.GetChurnControlStatistics().ReleaseCount);

        var live = world.CreateEntity(new A { Value = 17 });
        Assert.Equal(0, world.TrimChurnMemory());
        var active = world.GetChurnControlStatistics();
        Assert.Equal(4, active.AllocationCount);
        Assert.Equal(1, active.ReuseCount);
        Assert.Equal(3, active.ReleaseCount);
        Assert.Equal(native + pinned, active.OccupiedChunkCapacityBytes);
        Assert.Equal(active.OverBudgetBytes, active.UnreclaimableOverBudgetBytes);
        Assert.Equal(17, world.Get<A>(live).Value);
        world.DestroyEntity(live);
        Assert.Equal(4, world.GetChurnControlStatistics().ReleaseCount);
        Assert.Equal(0, world.GetStorageStatistics().NativeBytes);

        var invalid = new WorldOptions(1).WithChurnControl(new ChurnControlOptions(0, 1,
            new[] { new ChurnStartupBlock(native, 1) }));
        Assert.Throws<ArgumentException>(() => new World(invalid));
    }

    [Fact]
    public void FailedBackingAllocationPreservesNewAndRecycledIds()
    {
        var options = new WorldOptions(int.MaxValue).WithChurnControl(new ChurnControlOptions(0));
        using (var empty = new World(options))
        {
            Assert.Throws<OutOfMemoryException>(() => empty.CreateEntity());
            Assert.Equal(0, empty.OpenCheckpoint().SlotCount);
            Assert.Equal(0, empty.GetChurnControlStatistics().AllocationCount);
        }

        using var restored = World.RestoreCheckpoint(new[] { new CheckpointSlot(2, false) }, new[] { 0 }, options);
        Assert.Throws<OutOfMemoryException>(() => restored.CreateEntity());
        var checkpoint = restored.OpenCheckpoint();
        Assert.Equal(1, checkpoint.SlotCount);
        Assert.Equal(2, checkpoint.GetSlot(0).Version);
        Assert.False(checkpoint.GetSlot(0).Alive);
        Span<int> free = stackalloc int[1];
        checkpoint.CopyFreeIds(free);
        Assert.Equal(0, free[0]);
        Assert.Equal(0, restored.GetStorageStatistics().NativeBytes);
    }

    [Fact]
    public unsafe void MovingAnActiveChunkInTheQueryListPreservesRunnerAndAccessorAddresses()
    {
        using var world = Controlled(1);
        var a = world.CreateEntity(new A { Value = 1 });
        var b = world.CreateEntity(new A { Value = 2 });
        var c = world.CreateEntity(new A { Value = 3 });
        var access = world.GetAccessor<A>();
        world.DestroyEntity(a);
        Assert.Equal(2, access.Get(b).Value);
        Assert.Equal(3, access.Get(c).Value);
        Assert.Equal(0, world.TrimChurnMemory());
        Assert.Equal(3, world.Get<A>(c).Value);

        var workload = new DeterministicWorkload<int>();
        workload.AddSystem(SystemStage.Update, new DeterministicQuery().With<A>().ReadWrite<A>(), new Advance());
        using var runner = new DeterministicRunner<int>(world, workload);
        runner.BeginTick(1, 0, Fixed.FromInt32(1));
        Assert.Throws<InvalidOperationException>(() => world.TrimChurnMemory());
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(int.MaxValue));
        Assert.Equal(12, access.Get(b).Value);
        Assert.Equal(13, access.Get(c).Value);

        world.Add(c, new B { Value = 99 });
        Assert.Equal(13, access.Get(c).Value);
        Assert.Equal(99, world.Get<B>(c).Value);
        Assert.True(world.Remove<B>(c));
        Assert.Equal(13, access.Get(c).Value);
        Assert.False(world.Has<B>(c));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(256)]
    public unsafe void RandomizedStructuralOperationsMatchLegacy(int capacity)
    {
        using var legacy = new World(new WorldOptions(capacity));
        using var controlled = Controlled(capacity);
        var legacyAccess = legacy.GetAccessor<A>();
        var controlledAccess = controlled.GetAccessor<A>();
        var random = new Random(7319);
        var pairs = new List<(Entity Legacy, Entity Controlled)>();
        for (var step = 0; step < 900; step++)
        {
            var operation = pairs.Count == 0 ? 0 : random.Next(5);
            if (operation == 0)
            {
                var a = new A { Value = random.Next() };
                var expected = legacy.CreateEntity(a);
                var actual = controlled.CreateEntity(a);
                Assert.Equal((expected.Id, expected.Version), (actual.Id, actual.Version));
                pairs.Add((expected, actual));
                continue;
            }

            var index = random.Next(pairs.Count);
            var (left, right) = pairs[index];
            switch (operation)
            {
                case 1:
                    legacy.DestroyEntity(left);
                    controlled.DestroyEntity(right);
                    Assert.False(controlledAccess.Has(right));
                    pairs.RemoveAt(index);
                    break;
                case 2:
                    var b = new B { Value = step };
                    legacy.Set(left, b);
                    controlled.Set(right, b);
                    break;
                case 3:
                    Assert.Equal(legacy.Remove<B>(left), controlled.Remove<B>(right));
                    break;
                case 4:
                    var a = new A { Value = -step };
                    legacy.Set(left, a);
                    controlled.Set(right, a);
                    break;
            }

            foreach (var (expected, actual) in pairs)
            {
                Assert.Equal(legacyAccess.Get(expected).Value, controlledAccess.Get(actual).Value);
                Assert.Equal(legacy.TryGet(expected, out B legacyB), controlled.TryGet(actual, out B controlledB));
                Assert.Equal(legacyB.Value, controlledB.Value);
            }
            Assert.Equal(WithoutB(legacy).Order(), WithoutB(controlled).Order());
        }

        static List<int> WithoutB(World world)
        {
            var ids = new List<int>();
            foreach (var chunk in world.QueryBuilder().With<A>().Without<B>().Build().Chunks())
                for (var row = 0; row < chunk.Count; row++)
                    ids.Add(chunk.Entities[row]);
            return ids;
        }
    }

    private struct Advance : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            for (var i = 0; i < job.Count; i++)
                job.Write(i, new A { Value = job.Read<A>(i).Value + 10 });
        }
    }

    private static World Controlled(int capacity) => new World(new WorldOptions(capacity)
        .WithChurnControl(new ChurnControlOptions(long.MaxValue)));

    private static unsafe nint Address<T>(World world) where T : unmanaged
    {
        foreach (var chunk in world.Query<T>().Chunks()) return (nint)chunk.Component1;
        throw new InvalidOperationException("A live chunk was expected.");
    }

    private static Entity[] Fill<T>(World world, int count, Func<int, T> value) where T : unmanaged
    {
        var entities = new Entity[count];
        for (var i = 0; i < count; i++) entities[i] = world.CreateEntity(value(i));
        return entities;
    }

    private static void Empty(World world, Entity[] entities)
    {
        foreach (var entity in entities) world.DestroyEntity(entity);
    }

    private struct A { internal int Value; }
    private struct B { internal int Value; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct Wide { internal int Value; }
}
