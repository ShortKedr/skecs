using System.Xml.Linq;
using SKECS;
using SKECS.Profiler;

namespace Skecs.Samples.Profiler;

internal static class Sample
{
    private const int EntityCount = 4;

    public static Task RunAsync(string[] args)
    {
        var outputPath = args switch
        {
            [] => null,
            ["--output", var path] when !string.IsNullOrWhiteSpace(path) => Path.GetFullPath(path),
            _ => throw new ArgumentException("Usage: profiler [--output <new-trace.xml>]")
        };

        var session = new ProfilerSession(eventCapacity: 256);
        RecordEntityChurn(session);

        using var trace = new MemoryStream();
        session.Export(trace, scenario: "entity-churn", workloadId: "profiler-sample-v1");
        trace.Position = 0;
        VerifyTrace(trace);

        if (outputPath is not null)
        {
            trace.Position = 0;
            using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
            trace.CopyTo(file);
            Console.WriteLine($"Trace written to {outputPath}");
        }

        Console.WriteLine(
            $"Verified three profiler frames: {EntityCount} rows created, moved, and destroyed. No events dropped.");
        return Task.CompletedTask;
    }

    private static void RecordEntityChurn(ProfilerSession session)
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2), session.Buffer);
        var entities = new Entity[EntityCount];

        session.BeginFrame();
        for (var index = 0; index < entities.Length; index++)
        {
            entities[index] = world.CreateEntity(new Position(index));
        }
        session.EndFrame();

        // Adding a component moves each entity to a different archetype.
        session.BeginFrame();
        foreach (var entity in entities)
        {
            world.Add(entity, new Selected());
        }
        session.EndFrame();

        session.BeginFrame();
        foreach (var entity in entities)
        {
            world.DestroyEntity(entity);
        }
        session.EndFrame();
    }

    private static void VerifyTrace(Stream trace)
    {
        var root = XDocument.Load(trace).Root;
        if (root?.Name != "skecs-profiler-trace" || (int?)root.Attribute("droppedEvents") != 0)
        {
            throw new InvalidDataException("Expected a complete profiler trace.");
        }

        var events = root.Elements("event").ToArray();
        VerifyFrame(events, frame: 0, rowEvent: "rowCreated");
        VerifyFrame(events, frame: 1, rowEvent: "rowMoved");
        VerifyFrame(events, frame: 2, rowEvent: "rowDestroyed");
    }

    private static void VerifyFrame(XElement[] events, int frame, string rowEvent)
    {
        var start = Array.FindIndex(events, entry =>
            (string?)entry.Attribute("kind") == "frameStarted" && (int?)entry.Attribute("frame") == frame);
        var end = Array.FindIndex(events, entry =>
            (string?)entry.Attribute("kind") == "frameCompleted" && (int?)entry.Attribute("frame") == frame);
        if (start < 0 || end <= start)
        {
            throw new InvalidDataException($"The trace is missing boundaries for frame {frame}.");
        }

        var matchingEvents = events[(start + 1)..end].Count(entry => (string?)entry.Attribute("kind") == rowEvent);
        if (matchingEvents != EntityCount)
        {
            throw new InvalidDataException($"Expected {EntityCount} {rowEvent} events inside frame {frame}.");
        }
    }

    private readonly record struct Position(int X);

    private readonly struct Selected
    {
    }
}
