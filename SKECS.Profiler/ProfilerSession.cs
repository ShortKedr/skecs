using System;
using System.Diagnostics;
using System.IO;

namespace SKECS.Profiler;

/// <summary>A bounded trace of one world and timings reported by its host.</summary>
/// <remarks>Create one session per run. Record and export on the world's thread.</remarks>
public sealed class ProfilerSession
{
    private long _frameStart;
    private int _nextFrame;
    private bool _frameOpen;

    public ProfilerSession(int eventCapacity)
    {
        Buffer = new DiagnosticBuffer(eventCapacity);
    }

    public DiagnosticBuffer Buffer { get; }

    /// <summary>Opens the next zero-based frame. Events outside frames are retained too.</summary>
    public void BeginFrame()
    {
        if (_frameOpen) throw new InvalidOperationException("The previous frame is still open.");
        if (_nextFrame == int.MaxValue) throw new InvalidOperationException("The frame index is exhausted.");
        Buffer.RecordFrameStart(_nextFrame);
        _frameOpen = true;
        _frameStart = Stopwatch.GetTimestamp();
    }

    /// <summary>Closes the frame and returns its elapsed stopwatch ticks.</summary>
    public long EndFrame()
    {
        var frameEnd = Stopwatch.GetTimestamp();
        if (!_frameOpen) throw new InvalidOperationException("No frame is open.");
        var duration = frameEnd - _frameStart;
        Buffer.RecordFrameEnd(_nextFrame, duration);
        _nextFrame++;
        _frameOpen = false;
        return duration;
    }

    /// <summary>Records host-timed work such as a named system or a whole query.</summary>
    public void RecordDuration(string name, long elapsedStopwatchTicks)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A measurement name is required.", nameof(name));
        if (elapsedStopwatchTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedStopwatchTicks));
        Buffer.RecordDuration(name, elapsedStopwatchTicks);
    }

    /// <summary>Writes the captured prefix to a stream and leaves that stream open.</summary>
    public void Export(Stream destination, string scenario, string workloadId)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (string.IsNullOrEmpty(scenario)) throw new ArgumentException("A scenario name is required.", nameof(scenario));
        if (string.IsNullOrEmpty(workloadId)) throw new ArgumentException("A workload identifier is required.", nameof(workloadId));
        if (_frameOpen) throw new InvalidOperationException("Close the current frame before export.");
        TraceExport.Write(destination, this, scenario, workloadId);
    }
}
