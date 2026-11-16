namespace SKECS.Serialization.Tests;

public sealed partial class CheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckpointPreservesAllocatorAndChargesChurnStartupMemory(bool json)
    {
        using var source = new World();
        var queuedFirst = source.CreateEntity();
        var queuedLast = source.CreateEntity();
        var active = source.CreateEntity(19);
        source.DestroyEntity(queuedFirst);
        source.DestroyEntity(queuedLast);
        var registry = CreateRegistry();
        var bytes = Save(source, registry, json);
        var options = new CheckpointReadOptions
        {
            WorldOptions = new WorldOptions(1).WithChurnControl(new ChurnControlOptions(4096,
                startupReserveBytes: 312, startupBlocks: [new ChurnStartupBlock(31, 8)],
                initialEntityCapacity: 32))
        };
        using var restored = Load(bytes, registry, json, options);
        Assert.Equal(19, restored.Get<int>(restored.BindEntity(active.Id, active.Version)));
        Assert.Equal(bytes, Save(restored, registry, json));
        AssertHandle(source.CreateEntity(), restored.CreateEntity());
        AssertHandle(source.CreateEntity(), restored.CreateEntity());
        Assert.True(restored.GetChurnControlStatistics().Enabled);

        options.Limits.MaxWorkingBytes = 3000;
        Assert.Throws<InvalidDataException>(() => Load(bytes, registry, json, options));
    }
}
