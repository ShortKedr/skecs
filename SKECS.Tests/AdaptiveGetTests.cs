using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class AdaptiveGetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetValidatesTheHandleBeforeResolvingColumnOrPackedStorage(bool usePacked)
    {
        var placement = new ComponentPlacementOptions().AllowAdaptiveStorage<Effect>();
        if (usePacked) placement = placement.WithPackedPools(typeof(Effect));
        var options = new WorldOptions(1).WithChurnControl(new ChurnControlOptions(65_536))
            .WithComponentPlacement(placement);
        using var world = new World(options);
        using var other = new World(options);
        var first = world.CreateEntity(new Position(7), new Effect(11));
        var survivor = world.CreateEntity(new Position(13), new Effect(17));
        var foreign = other.CreateEntity(new Position(19), new Effect(23));
        Assert.Equal((first.Id, first.Version), (foreign.Id, foreign.Version));
        Reject(foreign);
        Reject(new Entity(first.Id, first.Version));

        world.Get<Position>(survivor).Value++;
        world.Get<Effect>(survivor).Value++;
        world.DestroyEntity(first);
        var next = world.CreateEntity(new Position(29), new Effect(31));
        Assert.Equal(first.Id, next.Id);
        Assert.NotEqual(first.Version, next.Version);
        Reject(first);
        Assert.Equal(14, world.Get<Position>(survivor).Value);
        Assert.Equal(18, world.Get<Effect>(survivor).Value);
        Assert.Equal(29, world.Get<Position>(next).Value);
        Assert.Equal(31, world.Get<Effect>(next).Value);

        using (var runner = new DeterministicRunner<int>(world, new DeterministicWorkload<int>()))
        {
            runner.BeginTick(1, 0, Fixed.FromInt32(1));
            Reject(next);
            Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(int.MaxValue));
        }
        Assert.True(world.Remove<Effect>(next));
        Assert.Throws<InvalidOperationException>(() => world.Get<Effect>(next));
        Assert.Equal(29, world.Get<Position>(next).Value);
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Get<Position>(next));
        Assert.Throws<ObjectDisposedException>(() => world.Get<Effect>(survivor));

        void Reject(Entity entity)
        {
            Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
            Assert.Throws<InvalidOperationException>(() => world.Get<Effect>(entity));
        }
    }

    private record struct Position(int Value);
    private record struct Effect(int Value);
}
