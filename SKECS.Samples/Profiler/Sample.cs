using System.Xml.Linq;
using SKECS;
using SKECS.Profiler;

namespace Skecs.Samples.Profiler;

internal static class Sample
{
    public static Task RunAsync(string[] args)
    {
        var outputPath = args switch
        {
            [] => null,
            ["--output", var path] when !string.IsNullOrWhiteSpace(path) => Path.GetFullPath(path),
            _ => throw new ArgumentException("Usage: profiler [--output <new-trace.xml>]")
        };

        const int entityCount = 4;
        var session = new ProfilerSession(eventCapacity: 256);
        var entities = new Entity[entityCount];
        using (var world = new World(new WorldOptions(chunkCapacity: 2), session.Buffer))
        {
            session.BeginFrame();
            for (var i = 0; i < entities.Length; i++)
                entities[i] = world.CreateEntity(new Position(i));
            session.EndFrame();

            session.BeginFrame();
            foreach (var entity in entities)
                world.Add(entity, new Selected());
            session.EndFrame();

            session.BeginFrame();
            foreach (var entity in entities)
                world.DestroyEntity(entity);
            session.EndFrame();
        }

        using var trace = new MemoryStream();
        session.Export(trace, scenario: "entity-churn", workloadId: "profiler-sample-v1");
        trace.Position = 0;
        var root = XDocument.Load(trace).Root;
        if (root?.Name != "skecs-profiler-trace" || (int?)root.Attribute("droppedEvents") != 0)
            throw new InvalidDataException("Expected a complete profiler trace.");

        var events = root.Elements("event").ToArray();
        VerifyFrame(events, 0, "rowCreated", entityCount);
        VerifyFrame(events, 1, "rowMoved", entityCount);
        VerifyFrame(events, 2, "rowDestroyed", entityCount);

        if (outputPath is not null)
        {
            trace.Position = 0;
            using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
            trace.CopyTo(file);
            Console.WriteLine($"Trace written to {outputPath}");
        }

        Console.WriteLine($"Verified three profiler frames: {entityCount} rows created, moved, and destroyed. No events dropped.");
        return Task.CompletedTask;
    }

    private static void VerifyFrame(XElement[] events, int frame, string rowEvent, int expectedCount)
    {
        var start = Array.FindIndex(events, entry => (string?)entry.Attribute("kind") == "frameStarted" &&
            (int?)entry.Attribute("frame") == frame);
        var end = Array.FindIndex(events, entry => (string?)entry.Attribute("kind") == "frameCompleted" &&
            (int?)entry.Attribute("frame") == frame);
        if (start < 0 || end <= start ||
            events[(start + 1)..end].Count(entry => (string?)entry.Attribute("kind") == rowEvent) != expectedCount)
            throw new InvalidDataException($"Expected {expectedCount} {rowEvent} events inside frame {frame}.");
    }

    private readonly record struct Position(int X);
    private readonly struct Selected { }
}
