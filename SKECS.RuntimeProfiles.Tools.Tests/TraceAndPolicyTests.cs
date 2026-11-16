using System.Xml.Linq;
using SKECS.Profiler;

namespace SKECS.RuntimeProfiles.Tools.Tests;

public sealed class TraceAndPolicyTests
{
    private const int Capacity = 2;

    [Fact]
    public void PeakUsesSimultaneousOccupancyAndRetainsCategoryBreakdown()
    {
        using var stream = Capture((world, profiler) =>
        {
            world.GetAccessor<First>();
            profiler.BeginFrame();
            var first = world.CreateEntity(new First(1));
            world.DestroyEntity(first);
            profiler.EndFrame();
            profiler.BeginFrame();
            world.CreateEntity(new Second(2));
            profiler.EndFrame();
        });
        var trace = TraceReader.Read(stream);
        Assert.Equal("case", trace.Header.Scenario);
        Assert.Equal("fixed-input", trace.Header.WorkloadId);
        Assert.Equal(2, trace.Frames);
        Assert.Equal(0, trace.LowerBound.Frame);
        Assert.Equal(31, trace.LowerBound.OccupiedNative);
        Assert.Equal(8 * Capacity, trace.LowerBound.OccupiedPinned);
        Assert.True(trace.LowerBound.EntityRecords > 0);
        Assert.True(trace.LowerBound.FreeIds > 0);
        Assert.True(trace.LowerBound.Accessors > 0);
        Assert.Equal(trace.LowerBound.Total + 31 + 8 * Capacity, trace.PeakRetained);
    }

    [Fact]
    public void MoveObservesDestinationBeforeReturningSource()
    {
        using var stream = Capture((world, profiler) =>
        {
            var entity = world.CreateEntity(new First(1));
            profiler.BeginFrame();
            world.Add(entity, new Second(2));
            profiler.EndFrame();
        });
        var trace = TraceReader.Read(stream);
        Assert.Equal(0, trace.LowerBound.Frame);
        Assert.Equal(2 * 31, trace.LowerBound.OccupiedNative);
        Assert.Equal(16 * Capacity, trace.LowerBound.OccupiedPinned);
    }

    [Fact]
    public void VersionTwoTracksStartupReserveCrossArchetypeReuseAndReleaseOnce()
    {
        const int oversized = 35;
        var options = new WorldOptions(Capacity).WithChurnControl(new ChurnControlOptions(long.MaxValue,
            oversized + 8 * Capacity, [new ChurnStartupBlock(oversized, 1)], initialEntityCapacity: 4));
        long expected = 0;
        using var stream = Capture((world, profiler) =>
        {
            profiler.BeginFrame();
            world.DestroyEntity(world.CreateEntity(new First(1)));
            world.CreateEntity(new Second(2));
            profiler.EndFrame();
            var storage = world.GetChurnControlStatistics();
            Assert.Equal(1, storage.AllocationCount);
            Assert.Equal(2, storage.ReuseCount);
            expected = storage.RetainedBytes;
        }, options);
        var document = XDocument.Load(stream);
        stream.Position = 0;
        var trace = TraceReader.Read(stream);
        Assert.Equal(2, trace.Header.SchemaVersion);
        Assert.Equal(oversized, trace.LowerBound.OccupiedNative);
        Assert.Equal(expected, trace.PeakRetained);
        Assert.Empty(trace.Changes);

        var rent = document.Root!.Elements().First(element => (string?)element.Attribute("kind") == "chunkReused");
        rent.SetAttributeValue("backing", 999);
        using var edited = new MemoryStream();
        document.Save(edited);
        edited.Position = 0;
        Assert.Throws<InvalidDataException>(() => TraceReader.Read(edited));
    }

    [Theory]
    [InlineData("schemaVersion", "4")]
    [InlineData("schemaVersion", "99")]
    [InlineData("droppedEvents", "1")]
    [InlineData("recordedEvents", "1000")]
    [InlineData("architecture", "")]
    public void RejectsUnsupportedLostOrIncompleteTrace(string key, string value)
    {
        using var clean = Capture((_, profiler) => { profiler.BeginFrame(); profiler.EndFrame(); });
        var document = XDocument.Load(clean);
        document.Root!.SetAttributeValue(key, value);
        using var edited = new MemoryStream();
        document.Save(edited);
        edited.Position = 0;
        Assert.Throws<InvalidDataException>(() => TraceReader.Read(edited));
    }

    [Fact]
    public void SimulationUsesPhysicalReuseAndReportsInfeasibleMemoryLimit()
    {
        using var stream = Capture((world, profiler) =>
        {
            profiler.BeginFrame();
            var first = world.CreateEntity(new First(1));
            world.DestroyEntity(first);
            profiler.EndFrame();
            profiler.BeginFrame();
            world.CreateEntity(new Second(2));
            profiler.EndFrame();
        });
        var trace = TraceReader.Read(stream);
        var scenario = new Scenario("case", "separated", 7, 1, 1, 1, false, 1);
        var entry = new ScenarioTrace(scenario, trace);
        var result = PolicySearch.Simulate(entry, Policy.Generic(trace.LowerBound.Total));
        Assert.Equal(1, result.EarlyAllocations);
        Assert.Equal(0, result.MeasuredAllocations);
        Assert.Equal(trace.LowerBound.Total, result.PeakRetained);
        var error = Assert.Throws<InvalidDataException>(() => PolicySearch.Propose([entry], trace.LowerBound.Total - 1));
        Assert.Contains($"case frame {trace.LowerBound.Frame}", error.Message);
        Assert.NotEmpty(PolicySearch.Propose([entry], null));
    }

    [Fact]
    public void BenchmarkWorkloadMatchesStateAcrossPoliciesAndCapturesTheRealMemoryBenefit()
    {
        var scenario = new Scenario("separate", "separated", 1729, 9, 4, 12, false, 1);
        var baselineRequest = new RunRequest(scenario, 4, 2048, null);
        var baseline = ScenarioRunner.CheckState(baselineRequest);
        var controlled = ScenarioRunner.CheckState(baselineRequest with { Policy = Policy.Generic(long.MaxValue) });

        Assert.Equal(1, baseline.Diagnostics.Version);
        Assert.Equal(2, controlled.Diagnostics.Version);
        Assert.Equal(scenario.WarmupFrames + scenario.Frames + 1, baseline.StateHashes.Length);
        Assert.Equal(baseline.StateHashes, controlled.StateHashes);
        Assert.True(controlled.Diagnostics.PeakRetained < baseline.Diagnostics.PeakRetained);
    }

    [Fact]
    public void VersionThreeTracksCrossStorageReuseAndCountsTheSharedIndexOnce()
    {
        var placement = new ComponentPlacementOptions().AllowAdaptiveStorage<Packed>().AllowAdaptiveStorage<Marker>()
            .WithPackedPools(typeof(Packed), typeof(Marker));
        var options = new WorldOptions(Capacity).WithChurnControl(new ChurnControlOptions(long.MaxValue, maxOversizePercent: 0))
            .WithComponentPlacement(placement);
        long peak = 0;
        using var stream = Capture((world, profiler) =>
        {
            profiler.BeginFrame();
            world.DestroyEntity(world.CreateEntity(new First(1)));
            var a = world.CreateEntity(new Packed(1));
            world.Add(a, new Marker(1));
            world.CreateEntity(new Packed(2));
            var c = world.CreateEntity(new Packed(3));
            peak = world.GetChurnControlStatistics().RetainedBytes;
            world.Remove<Packed>(c);
            world.CreateEntity(new Second(2));
            peak = Math.Max(peak, world.GetChurnControlStatistics().RetainedBytes);
            profiler.EndFrame();
        }, options);
        var xml = XDocument.Load(stream);
        stream.Position = 0;
        var trace = TraceReader.Read(stream);
        Assert.Equal(3, trace.Header.SchemaVersion);
        Assert.Equal(2, trace.PackedLayouts.Count);
        Assert.Equal(peak, trace.PeakRetained);
        Assert.True(trace.LowerBound.PackedPresence > 0);
        var events = xml.Root!.Elements().ToArray();
        var reusedPage = events.First(entry => (string?)entry.Attribute("kind") == "packedPageReused");
        var releasedPage = events.First(entry => (string?)entry.Attribute("kind") == "packedPageReturned");
        Assert.Contains(events, entry => (string?)entry.Attribute("kind") == "chunkAllocated" &&
            (long)entry.Attribute("backing")! == (long)reusedPage.Attribute("backing")!);
        Assert.Contains(events, entry => (string?)entry.Attribute("kind") == "chunkReused" &&
            (long)entry.Attribute("backing")! == (long)releasedPage.Attribute("backing")!);

        releasedPage.SetAttributeValue("backing", 999);
        using var invalid = new MemoryStream();
        xml.Save(invalid);
        invalid.Position = 0;
        Assert.Throws<InvalidDataException>(() => TraceReader.Read(invalid));
    }

    private static MemoryStream Capture(Action<World, ProfilerSession> work, WorldOptions? options = null)
    {
        var profiler = new ProfilerSession(128);
        using (var world = new World(options ?? new WorldOptions(Capacity), profiler.Buffer)) work(world, profiler);
        var stream = new MemoryStream();
        profiler.Export(stream, "case", "fixed-input");
        stream.Position = 0;
        return stream;
    }

    private readonly record struct First(int Value);
    private readonly record struct Second(int Value);
    private readonly record struct Packed(int Value);
    private readonly record struct Marker(int Value);
}
