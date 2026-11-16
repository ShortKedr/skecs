using System.Xml.Linq;
using SKECS.Profiler;

namespace SKECS.Profiler.Tests;

public sealed class WorldTraceTests
{
    [Fact]
    public void TraceReconstructsOccupancyAndReportsCopiesAndRetainedStorage()
    {
        var session = new ProfilerSession(128);
        using var world = new World(new WorldOptions(chunkCapacity: 2), session.Buffer);
        world.GetAccessor<Position>();

        session.BeginFrame();
        var a = world.CreateEntity(new Position(10));
        var b = world.CreateEntity(new Position(20));
        world.Add(a, new Speed(2));
        Assert.Equal(20, world.Get<Position>(b).Value);
        Assert.True(world.Remove<Speed>(a));
        world.DestroyEntity(b);
        Assert.Equal(10, world.Get<Position>(a).Value);
        world.DestroyEntity(a);
        var recycled = world.CreateEntity(new Position(30));
        Assert.Equal(a.Id, recycled.Id);
        Assert.NotEqual(a.Version, recycled.Version);
        session.EndFrame();

        var stats = world.GetStorageStatistics();
        world.Dispose();
        world.Dispose();
        using var stream = new MemoryStream();
        session.Export(stream, "churn", "test");
        stream.Position = 0;
        var trace = XDocument.Load(stream).Root!;
        var events = trace.Elements("event").ToArray();
        Assert.Equal(0, (long)trace.Attribute("droppedEvents")!);
        Assert.Equal(events.Length, Value(trace, "recordedEvents"));
        Assert.Equal(2, Value(events[0], "chunkCapacity"));
        Assert.Equal("worldDisposed", Kind(events[^1]));
        Assert.Single(events, entry => Kind(entry) == "worldDisposed");

        var definitions = events.Where(entry => Kind(entry) == "archetypeDefined").ToArray();
        Assert.Equal(stats.ArchetypeCount, definitions.Length);
        Assert.Equal(new long[] { 0, 1, 2 }, definitions.Select(entry => Value(entry, "componentCount")));
        var components = events.Where(entry => Kind(entry) == "archetypeComponent").ToArray();
        Assert.Contains(components, entry =>
            (string?)entry.Attribute("type") == typeof(Position).FullName &&
            (string?)entry.Attribute("assembly") == typeof(Position).Assembly.GetName().Name &&
            Value(entry, "size") == 4 && Value(entry, "alignment") == 4);

        var moves = events.Where(entry => Kind(entry) == "rowMoved").ToArray();
        Assert.Equal(new long[] { 4, 4 }, moves.Select(entry => Value(entry, "copiedBytes")));
        Assert.Equal(new long[] { 12, 0 }, moves.Select(entry => Value(entry, "swapBytes")));
        Assert.Equal(new long[] { 12, 0 }, events.Where(entry => Kind(entry) == "rowDestroyed")
            .Select(entry => Value(entry, "swapBytes")));

        var allocations = events.Where(entry => Kind(entry) == "chunkAllocated").ToArray();
        Assert.Equal(stats.ChunkCount, allocations.Length);
        Assert.Equal(stats.NativeBytes, allocations.Sum(entry => Value(entry, "nativeBytes")));
        Assert.All(allocations, entry => Assert.Equal(16, Value(entry, "pinnedBytes")));

        var tables = events.Where(entry => Kind(entry) == "tableResized")
            .ToDictionary(entry => (string)entry.Attribute("table")!);
        Assert.Equal(4, Value(tables["entityRecords"], "capacity"));
        Assert.True(Value(tables["entityRecords"], "payloadBytes") >= 4 * 4);
        Assert.Equal(4, Value(tables["freeEntityIds"], "capacity"));
        Assert.Equal(16, Value(tables["freeEntityIds"], "payloadBytes"));
        Assert.Equal(16, Value(tables["accessor"], "capacity"));
        Assert.Equal(stats.AccessorBufferBytes, Value(tables["accessor"], "payloadBytes"));

        var occupancy = new Dictionary<(long Archetype, long Chunk), int>();
        var observedEmpty = new HashSet<(long Archetype, long Chunk)>();
        var observedReuse = false;
        foreach (var entry in events)
        {
            switch (Kind(entry))
            {
                case "chunkAllocated":
                    occupancy.Add(Slot(entry), 0);
                    break;
                case "rowCreated":
                    observedReuse |= observedEmpty.Contains(Slot(entry));
                    occupancy[Slot(entry)]++;
                    break;
                case "rowMoved":
                    Decrement(Slot(entry));
                    occupancy[(Value(entry, "targetArchetype"), Value(entry, "targetChunk"))]++;
                    break;
                case "rowDestroyed":
                    Decrement(Slot(entry));
                    break;
                case "frameCompleted":
                    Assert.Equal(stats.EntityCount, occupancy.Values.Sum());
                    Assert.Single(occupancy, pair => pair.Value > 0);
                    break;
                case "chunkReleased":
                    Assert.True(occupancy.Remove(Slot(entry)));
                    break;
            }
            Assert.All(occupancy.Values, count => Assert.InRange(count, 0, 2));
        }
        Assert.True(observedReuse);
        Assert.Empty(occupancy);

        void Decrement((long, long) slot)
        {
            if (--occupancy[slot] == 0) observedEmpty.Add(slot);
        }
    }

    [Fact]
    public void FullTraceBufferDoesNotChangeWorldBehavior()
    {
        var session = new ProfilerSession(1);
        using (var world = new World(default, session.Buffer))
        {
            var entity = world.CreateEntity(new Position(5));
            Assert.Equal(5, world.Get<Position>(entity).Value);
            world.DestroyEntity(entity);
            Assert.False(world.IsAlive(entity));
            Assert.Throws<InvalidOperationException>(() => new World(default, session.Buffer));
        }

        Assert.Equal(1, session.Buffer.Count);
        Assert.Equal(DiagnosticEventKind.WorldStarted, session.Buffer[0].Kind);
        Assert.True(session.Buffer.DroppedEvents > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckpointRetainsNextAllocationOrderAcrossFreeTableGrowth(bool record)
    {
        var options = new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true);
        var session = record ? new ProfilerSession(256) : null;
        using var world = session == null ? new World(options) : new World(options, session.Buffer);
        var entities = new Entity[7];
        for (var i = 0; i < entities.Length; i++) entities[i] = world.CreateEntity(new Position(i));
        foreach (var id in new[] { 3, 0, 5, 4, 1 }) world.DestroyEntity(entities[id]);

        var snapshot = world.OpenCheckpoint();
        var free = new int[snapshot.FreeCount];
        snapshot.CopyFreeIds(free);
        Assert.Equal(new[] { 1, 4, 5, 0, 3 }, free);
        var slots = new CheckpointSlot[snapshot.SlotCount];
        for (var i = 0; i < slots.Length; i++) slots[i] = snapshot.GetSlot(i);

        using var restored = World.RestoreCheckpoint(slots, free, options);
        foreach (var expectedId in free)
        {
            var next = world.CreateEntity();
            var restoredNext = restored.CreateEntity();
            Assert.Equal(expectedId, next.Id);
            Assert.Equal(next.Id, restoredNext.Id);
            Assert.Equal(next.Version, restoredNext.Version);
        }

        Assert.True(world.DiagnosticsEnabled);
        if (session != null)
        {
            var freeTableCapacities = Enumerable.Range(0, session.Buffer.Count)
                .Select(index => session.Buffer[index])
                .Where(entry => entry.Kind == DiagnosticEventKind.TableResized && entry.Table == DiagnosticTable.FreeEntityIds)
                .Select(entry => entry.TableCapacity);
            Assert.Equal(new[] { 4, 8 }, freeTableCapacities);
            Assert.Equal(0, session.Buffer.DroppedEvents);
        }
    }

    private static string Kind(XElement element) => (string)element.Attribute("kind")!;
    private static long Value(XElement element, string name) => (long)element.Attribute(name)!;
    private static (long, long) Slot(XElement element) => (Value(element, "archetype"), Value(element, "chunk"));

    private readonly record struct Position(int Value);
    private readonly record struct Speed(int Value);
}
