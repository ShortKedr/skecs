using System.Xml.Linq;
using SKECS.Profiler;

namespace SKECS.Profiler.Tests;

public sealed class ProfilerSessionTests
{
    [Fact]
    public void ExportPreservesFrameOrderAndMeasurementNames()
    {
        var session = new ProfilerSession(8);
        session.RecordDuration("startup", 2);
        session.BeginFrame();
        session.RecordDuration("query <dense> & update", 3);
        var elapsed = session.EndFrame();
        session.BeginFrame();
        session.EndFrame();

        using var stream = new MemoryStream();
        session.Export(stream, "dense", "test-build");
        stream.Position = 0;
        var trace = XDocument.Load(stream).Root!;
        Assert.Equal("skecs-profiler-trace", trace.Name.LocalName);
        Assert.Equal(1, (int)trace.Attribute("schemaVersion")!);
        Assert.Equal("dense", (string?)trace.Attribute("scenario"));
        Assert.Equal("test-build", (string?)trace.Attribute("workloadId"));
        Assert.True((long)trace.Attribute("stopwatchFrequency")! > 0);
        Assert.Equal(0, (long)trace.Attribute("droppedEvents")!);

        var events = trace.Elements("event").ToArray();
        Assert.Equal(new[] { "duration", "frameStarted", "duration", "frameCompleted", "frameStarted", "frameCompleted" },
            events.Select(entry => (string?)entry.Attribute("kind")));
        Assert.Equal("query <dense> & update", (string?)events[2].Attribute("name"));
        Assert.Equal(3, (long)events[2].Attribute("elapsedTicks")!);
        Assert.Equal(elapsed, (long)events[3].Attribute("elapsedTicks")!);
        Assert.Equal(1, (int)events[5].Attribute("frame")!);
    }

    [Fact]
    public void FullBufferRetainsPrefixAndCountsFurtherEvents()
    {
        var session = new ProfilerSession(2);
        session.BeginFrame();
        session.RecordDuration("first", 10);
        session.EndFrame();
        session.BeginFrame();
        session.RecordDuration("second", 20);
        session.EndFrame();

        using var stream = new MemoryStream();
        session.Export(stream, "overflow", "test-build");
        stream.Position = 0;
        var trace = XDocument.Load(stream).Root!;
        Assert.Equal(2, (int)trace.Attribute("recordedEvents")!);
        Assert.Equal(4, (long)trace.Attribute("droppedEvents")!);
        Assert.Equal(new[] { "frameStarted", "duration" },
            trace.Elements("event").Select(entry => (string?)entry.Attribute("kind")));
    }

    [Fact]
    public void InvalidHostCallsDoNotRecordEventsOrChangeFrameNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProfilerSession(0));
        var session = new ProfilerSession(4);
        Assert.Throws<InvalidOperationException>(() => session.EndFrame());
        Assert.Throws<ArgumentException>(() => session.RecordDuration("", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RecordDuration("query", -1));
        Assert.Equal(0, session.Buffer.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Buffer[0]);

        session.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => session.BeginFrame());
        using var stream = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => session.Export(stream, "test", "test-build"));
        Assert.Equal(0, stream.Length);
        session.EndFrame();
        Assert.Equal(2, session.Buffer.Count);
    }
}
