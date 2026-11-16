using System.Runtime.InteropServices;

namespace SKECS.Tests;

public class WorldSafetyTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PackedValue
    {
        public short First;
        public byte Second;
    }

    private struct Marker
    {
        public byte Value;
    }

    [Fact]
    public void ChunkLayoutRejectsOverflowBeforeAllocatingNativeStorage()
    {
        using var multiplication = new World(new WorldOptions(chunkCapacity: int.MaxValue / sizeof(long) + 1));
        Assert.Throws<OverflowException>(() => multiplication.PrepareEntity<long, byte>());
        Assert.Equal(0, multiplication.GetStorageStatistics().ChunkCount);

        using var addition = new World(new WorldOptions(chunkCapacity: int.MaxValue / sizeof(long)));
        Assert.Throws<OverflowException>(() => addition.PrepareEntity<long, byte>());
        Assert.Equal(0, addition.GetStorageStatistics().ChunkCount);

        using var padding = new World(new WorldOptions(chunkCapacity: int.MaxValue / 2));
        Assert.Throws<OverflowException>(() => padding.PrepareEntity<byte, Marker>());
        Assert.Equal(0, padding.GetStorageStatistics().ChunkCount);
    }

    [Fact]
    public unsafe void ChunkColumnsAreAlignedAndPreservePackedComponents()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 3));
        world.CreateEntity(new Marker { Value = 1 }, new PackedValue { First = 2, Second = 3 }, 4L);
        world.CreateEntity(new Marker { Value = 5 }, new PackedValue { First = 6, Second = 7 }, 8L);
        var chunks = world.Query<Marker, PackedValue, long>().Chunks().GetEnumerator();
        Assert.True(chunks.MoveNext());
        var chunk = chunks.Current;
        Assert.Equal(2, chunk.Count);
        Assert.Equal((nuint)0, (nuint)chunk.Component1 % 16);
        Assert.Equal((nuint)0, (nuint)chunk.Component3 % 8);
        Assert.Equal(5, chunk.Component1[1].Value);
        Assert.Equal(6, chunk.Component2[1].First);
        Assert.Equal(7, chunk.Component2[1].Second);
        Assert.Equal(8L, chunk.Component3[1]);
    }

    [Fact]
    public void StaleForeignAndUnboundHandlesCannotAddressAWorldEntity()
    {
        using var world = new World();
        using var other = new World();
        var stale = world.CreateEntity(12);
        var foreign = other.CreateEntity(16);
        Assert.Equal(stale.Id, foreign.Id);
        Assert.Equal(stale.Version, foreign.Version);
        Assert.NotEqual(stale, foreign);
        var accessor = world.GetAccessor<int>();
        Reject(foreign);
        var unbound = new Entity(stale.Id, stale.Version);
        Assert.False(default(Entity).IsValid);
        Assert.False(unbound.IsValid);
        Reject(unbound);
        Assert.Equal(stale, world.BindEntity(unbound.Id, unbound.Version));

        world.DestroyEntity(stale);
        var current = world.CreateEntity(42);
        Assert.Equal(stale.Id, current.Id);
        Assert.NotEqual(stale.Version, current.Version);
        Reject(stale);
        Reject(world.BindEntity(int.MaxValue, current.Version));
        Assert.Equal(42, accessor.Get(current));

        var processor = new Collect();
        world.Query<int>().ForEach(ref processor);
        Assert.Equal(current, processor.Entity);

        var holder = world.CreateEntity(current);
        Assert.True(world.TryGet<Entity>(holder, out var stored));
        Assert.Equal(current, stored);
        Assert.True(world.IsAlive(stored));

        void Reject(Entity invalid)
        {
            Assert.False(world.IsAlive(invalid));
            Assert.False(world.TryGet<int>(invalid, out _));
            Assert.False(accessor.Has(invalid));
            Assert.False(accessor.TryGet(invalid, out _));
            Assert.Throws<InvalidOperationException>(() => accessor.Get(invalid));
            Assert.Throws<InvalidOperationException>(() => world.Get<int>(invalid));
            Assert.Throws<InvalidOperationException>(() => world.Has<int>(invalid));
            Assert.Throws<InvalidOperationException>(() => world.DestroyEntity(invalid));
            Assert.Throws<InvalidOperationException>(() => world.Set(invalid, 99));
            Assert.Throws<InvalidOperationException>(() => world.Add(invalid, new Marker()));
            Assert.Throws<InvalidOperationException>(() => world.Remove<int>(invalid));
        }
    }

    [Fact]
    public void DisposedWorldRejectsOperationsAndExistingQueries()
    {
        using var world = new World();
        var entity = world.CreateEntity(1, 2L, (byte)3);
        var factory = world.PrepareEntity<int, long>();
        var one = world.Query<int>();
        var two = world.Query<int, long>();
        var three = world.Query<int, long, byte>();
        var builder = world.QueryBuilder().With<int>();
        var description = builder.Build();
        var raw = description.Chunks();
        var typed = three.Chunks();
        var rawEnumerator = raw.GetEnumerator();
        var typedEnumerator = typed.GetEnumerator();
        Assert.True(rawEnumerator.MoveNext());
        Assert.True(typedEnumerator.MoveNext());

        world.Dispose();

        Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
        Assert.Throws<ObjectDisposedException>(() => world.TryGet<int>(entity, out _));
        Assert.Throws<ObjectDisposedException>(() => world.Get<int>(entity));
        Assert.Throws<ObjectDisposedException>(() => world.Set(entity, 2));
        Assert.Throws<ObjectDisposedException>(() => world.DestroyEntity(entity));
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity(1));
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity(1, 2L, (byte)3));
        Assert.Throws<ObjectDisposedException>(() => world.BindEntity(entity.Id, entity.Version));
        Assert.Throws<ObjectDisposedException>(() => world.Query<int>());
        Assert.Throws<ObjectDisposedException>(() => world.QueryBuilder());
        Assert.Throws<ObjectDisposedException>(() => world.AddSystem(SystemStage.Update, _ => { }));
        Assert.Throws<ObjectDisposedException>(() => world.RunAll());
        Assert.Throws<ObjectDisposedException>(() => factory.CreateBatch(Array.Empty<Entity>(), 1, 2L));
        Assert.Throws<ObjectDisposedException>(() => builder.Without<int>());
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
        Assert.Throws<ObjectDisposedException>(() => { _ = description.MatchCount; });
        Assert.Throws<ObjectDisposedException>(() => description.Chunks());
        Assert.Throws<ObjectDisposedException>(() => one.Chunks());
        Assert.Throws<ObjectDisposedException>(() => two.Chunks());
        Assert.Throws<ObjectDisposedException>(() => three.Chunks());
        Assert.Throws<ObjectDisposedException>(() => raw.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => typed.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => rawEnumerator.MoveNext());
        Assert.Throws<ObjectDisposedException>(() => typedEnumerator.MoveNext());
        Assert.Throws<ObjectDisposedException>(() => { _ = rawEnumerator.Current; });
        Assert.Throws<ObjectDisposedException>(() => { _ = typedEnumerator.Current; });
        var processor = new Collect();
        Assert.Throws<ObjectDisposedException>(() => one.ForEach(ref processor));
    }

    [Fact]
    public void DisposeDuringQueryCallbackIsRejectedAndWorldCanStillBeDisposed()
    {
        using var world = new World();
        var entity = world.CreateEntity(1);
        var processor = new DisposeWorld { World = world };
        Assert.Throws<InvalidOperationException>(() => world.Query<int>().ForEach(ref processor));
        Assert.Equal(1, world.Get<int>(entity));
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
    }

    private struct Collect : IQueryProcessor<int>
    {
        public Entity Entity;
        public void Execute(Entity entity, ref int component) => Entity = entity;
    }

    private struct DisposeWorld : IQueryProcessor<int>
    {
        public World World;
        public void Execute(Entity entity, ref int component) => World.Dispose();
    }
}
