using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace SKECS;

[EventSource(Name = "SKECS-DeterministicRunner")]
internal sealed class RunnerTrace : EventSource
{
    public static readonly RunnerTrace Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void PhaseCompleted(string phase, long elapsedTicks) => WriteEvent(1, phase, elapsedTicks);

    [NonEvent]
    public void Finish(string phase, long started) => PhaseCompleted(phase, Stopwatch.GetTimestamp() - started);
}
