using System.Runtime.InteropServices;
using SKECS.Numerics;

namespace SKECS.Tests;

public sealed unsafe class QueryChunkProcessorTests
{
    private struct Left { public int Value; }
    private struct Right { public int Value; }
    private struct Sum { public int Value; }
    private struct SecondArchetype;

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(256)]
    public void ForEachChunkAppliesInt32KernelAcrossChunksAndArchetypes(int capacity)
    {
        using var world = new World(new WorldOptions(capacity));
        var query = world.Query<Left, Right, Sum>();
        var entities = new Entity[capacity * 2 + 5];
        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = world.CreateEntity(
                new Left { Value = int.MaxValue - i },
                new Right { Value = i % 2 == 0 ? i + 10 : -i },
                new Sum { Value = -1 });
            if (i == entities.Length - 1)
                world.Add(entities[i], new SecondArchetype());
        }

        var scalar = new AddChunks { Mode = KernelMode.Scalar };
        query.ForEachChunk(ref scalar);
        var expected = new int[entities.Length];
        for (var i = 0; i < entities.Length; i++)
        {
            expected[i] = unchecked(world.Get<Left>(entities[i]).Value + world.Get<Right>(entities[i]).Value);
            Assert.Equal(expected[i], world.Get<Sum>(entities[i]).Value);
            world.Get<Sum>(entities[i]).Value = -1;
        }

        var automatic = new AddChunks();
        query.ForEachChunk(ref automatic);

        Assert.True(automatic.Chunks > 2);
        Assert.Equal(scalar.Chunks, automatic.Chunks);
        Assert.Equal(entities.Length, automatic.Rows);
        for (var i = 0; i < entities.Length; i++)
            Assert.Equal(expected[i], world.Get<Sum>(entities[i]).Value);
    }

    [Fact]
    public void OneAndTwoColumnProcessorsUseChunkOrderAndKeepStateByReference()
    {
        using var world = new World(new WorldOptions(2));
        var entities = Enumerable.Range(0, 5).Select(i => world.CreateEntity(
            new Left { Value = i }, new Right { Value = i * 3 })).ToArray();
        var order = new List<int>();
        foreach (var chunk in world.Query<Left>().Chunks())
            for (var i = 0; i < chunk.Count; i++)
                order.Add(chunk.Entities[i]);

        var one = new IncrementLeft { Entities = new List<int>() };
        world.Query<Left>().ForEachChunk(ref one);
        Assert.Equal(order, one.Entities);
        Assert.Equal(3, one.Chunks);

        var two = new AddToRight();
        world.Query<Left, Right>().ForEachChunk(ref two);
        Assert.Equal(3, two.Chunks);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(i + 1, world.Get<Left>(entities[i]).Value);
            Assert.Equal(i * 4 + 1, world.Get<Right>(entities[i]).Value);
        }
    }

    [Fact]
    public void ForEachChunkReleasesTheQueryGuardAfterCallbackFailure()
    {
        using var world = new World();
        var query = world.Query<Left>();
        var processor = new DisposeWorld { World = world };
        query.ForEachChunk(ref processor);
        Assert.Equal(0, processor.Calls);

        world.CreateEntity(new Left());
        Assert.Throws<InvalidOperationException>(() => query.ForEachChunk(ref processor));
        Assert.Equal(1, processor.Calls);
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => query.ForEachChunk(ref processor));
        Assert.Equal(1, processor.Calls);
    }

    private struct IncrementLeft : IQueryChunkProcessor<Left>
    {
        public List<int> Entities;
        public int Chunks;

        public void Execute(QueryChunkView<Left> chunk)
        {
            Chunks++;
            for (var i = 0; i < chunk.Count; i++)
            {
                Entities.Add(chunk.Entities[i]);
                chunk.Component1[i].Value++;
            }
        }
    }

    private struct AddToRight : IQueryChunkProcessor<Left, Right>
    {
        public int Chunks;

        public void Execute(QueryChunkView<Left, Right> chunk)
        {
            Chunks++;
            for (var i = 0; i < chunk.Count; i++)
                chunk.Component2[i].Value += chunk.Component1[i].Value;
        }
    }

    private struct AddChunks : IQueryChunkProcessor<Left, Right, Sum>
    {
        public KernelMode Mode;
        public int Chunks;
        public int Rows;

        public void Execute(QueryChunkView<Left, Right, Sum> chunk)
        {
            Chunks++;
            Rows += chunk.Count;
            NumericKernels.AddInt32(
                MemoryMarshal.Cast<Left, int>(new ReadOnlySpan<Left>(chunk.Component1, chunk.Count)),
                MemoryMarshal.Cast<Right, int>(new ReadOnlySpan<Right>(chunk.Component2, chunk.Count)),
                MemoryMarshal.Cast<Sum, int>(new Span<Sum>(chunk.Component3, chunk.Count)),
                Mode);
        }
    }

    private struct DisposeWorld : IQueryChunkProcessor<Left>
    {
        public World World;
        public int Calls;

        public void Execute(QueryChunkView<Left> chunk)
        {
            Calls++;
            World.Dispose();
        }
    }
}
