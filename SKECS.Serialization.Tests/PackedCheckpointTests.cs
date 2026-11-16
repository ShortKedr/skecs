namespace SKECS.Serialization.Tests;

public sealed partial class CheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackedCheckpointIsByteIdenticalAcrossPlacementsAndRestoresEitherLayout(bool json)
    {
        var schema = IntRegistry();
        var packedOptions = new WorldOptions(2)
            .WithChurnControl(new ChurnControlOptions(16_384))
            .WithComponentPlacement(new ComponentPlacementOptions().AllowAdaptiveStorage<int>().WithPackedPools(typeof(int)));
        using var baseline = new World(new WorldOptions(3));
        using var packed = new World(packedOptions);
        foreach (var world in new[] { baseline, packed })
        {
            var first = world.CreateEntity(12);
            var removed = world.CreateEntity(34);
            var last = world.CreateEntity(56);
            world.DestroyEntity(removed);
            world.Remove<int>(first);
            world.Set(last, 78);
        }

        var expected = Save(baseline, schema, json);
        var packedBytes = Save(packed, schema, json);
        Assert.Equal(expected, packedBytes);
        using var restoredPacked = Load(expected, schema, json, new CheckpointReadOptions { WorldOptions = packedOptions });
        using var restoredArchetype = Load(packedBytes, schema, json);
        Assert.Throws<InvalidOperationException>(() => restoredPacked.Query<int>());
        Assert.Equal(78, restoredPacked.Get<int>(restoredPacked.BindEntity(2, 1)));
        Assert.Equal(78, restoredArchetype.Get<int>(restoredArchetype.BindEntity(2, 1)));
        Assert.Equal(1, restoredPacked.CreateEntity().Id);
        Assert.Equal(1, restoredArchetype.CreateEntity().Id);
    }
}
