using System.Xml.Linq;
using SKECS.Profiler;

namespace SKECS.Profiler.Tests;

public sealed class ChurnTraceTests
{
    [Fact]
    public void VersionOneKeepsLegacySlotsAndVersionTwoTracksPhysicalBackingTransfers()
    {
        var legacy = Capture(churn: false);
        Assert.Equal("1", (string?)legacy.Attribute("schemaVersion"));
        Assert.Equal(3, legacy.Elements("event").Count(e => Kind(e) == "chunkAllocated"));
        Assert.DoesNotContain(legacy.Elements("event"), e => e.Attribute("backing") != null);

        var controlled = Capture(churn: true);
        Assert.Equal("2", (string?)controlled.Attribute("schemaVersion"));
        var structural = controlled.Elements("event").Where(e =>
            Kind(e) is "chunkPooled" or "chunkReused" or "pooledChunkAllocated" or "pooledChunkReleased").ToArray();
        Assert.Equal(new[] { "pooledChunkAllocated", "chunkReused", "chunkPooled", "chunkReused", "chunkPooled", "pooledChunkReleased" },
            structural.Select(Kind));
        Assert.Single(structural.Select(e => (long)e.Attribute("backing")!).Distinct());
        Assert.NotEqual((int)structural[1].Attribute("archetype")!, (int)structural[3].Attribute("archetype")!);
        var newPhysical = Assert.Single(controlled.Elements("event"), e => Kind(e) == "chunkAllocated");
        Assert.NotEqual((long)newPhysical.Attribute("backing")!, (long)structural[0].Attribute("backing")!);
        Assert.Single(controlled.Elements("event"), e => Kind(e) == "chunkReleased");
        Assert.Equal("worldDisposed", Kind(controlled.Elements("event").Last()));
    }

    private static string Kind(XElement node) => (string)node.Attribute("kind")!;

    private static XElement Capture(bool churn)
    {
        var session = new ProfilerSession(256);
        var options = new WorldOptions(1);
        if (churn)
            options = options.WithChurnControl(new ChurnControlOptions(long.MaxValue, 39, [new ChurnStartupBlock(31, 1)]));
        using (var world = new World(options, session.Buffer))
        {
            world.DestroyEntity(world.CreateEntity(17));
            world.DestroyEntity(world.CreateEntity(18U));
            world.CreateEntity((19L, 20L, 21L));
        }
        using var stream = new MemoryStream();
        session.Export(stream, "churn", "test");
        stream.Position = 0;
        return XDocument.Load(stream).Root!;
    }
}
