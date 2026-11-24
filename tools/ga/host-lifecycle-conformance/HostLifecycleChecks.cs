#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;

internal static class HostLifecycleChecks
{
    internal static DeterministicWorkload<HostInput> WithObserver(DeterministicWorkload<HostInput> workload) =>
        workload.AddJob(SystemStage.PostUpdate, new DeterministicQuery(), new Observer());

    internal static async Task<GaExecutionException> Exercise(Func<DeterministicWorkload<HostInput>> workload, string lane, int chunk,
        AuditedBatches? batches = null, GaPackedKernel? asyncKernel = null, bool checkVisibility = false,
        Action<string>? report = null)
    {
#if UNITY_2021_3_OR_NEWER
        bool singleWorker = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WebGLPlayer;
#else
        bool singleWorker = OperatingSystem.IsBrowser();
#endif
        using var host = new HostSession(workload, chunk, singleWorker ? 1 : 2);
        host.Reset();
        var initial = host.Render();
        VerifyFrame(initial, 0);

        var input = new HostInput { Divisor = 2 };
        if (batches is not null) batches.Hold = true;
        Require(host.Update(input) == DeterministicTickStatus.Pending, "Tick 1 completed within a one-job budget.");
        VerifyLease(host);
        input.Divisor = 4;
#if GA_BROWSER_CONFORMANCE
        if (checkVisibility)
        {
            Require(!BrowserVisibility.IsHidden(), "The GPU tick must start in a visible tab.");
            Console.WriteLine("WebGPU HostLifecycle waitingForHidden=ready");
            await BrowserVisibility.WaitForVisibility(true);
            host.Paused = BrowserVisibility.IsHidden();
        }
        else
#endif
        host.Paused = true;
        int submitted = batches?.Submitted ?? 0;
        for (int poll = 0; poll < 3; poll++)
        {
#if UNITY_2021_3_OR_NEWER
            await Task.Yield();
#endif
            Require(host.Update(input) == DeterministicTickStatus.Pending && host.Poll() == DeterministicTickStatus.Pending,
                "Paused host advanced its logical work.");
        }
        var pausedFrame = singleWorker ? host.Render() : await Task.Run(host.Render);
        Require((batches?.Submitted ?? 0) == submitted && host.Captures == 1 && ReferenceEquals(initial, pausedFrame),
            "Pause changed the published frame or queued additional work.");
#if GA_BROWSER_CONFORMANCE
        if (checkVisibility)
        {
            Require(BrowserVisibility.IsHidden(), "The driver restored the tab before polling it while hidden.");
            Console.WriteLine("WebGPU HostLifecycle hiddenPending=passed");
            await BrowserVisibility.WaitForVisibility(false);
            host.Paused = BrowserVisibility.IsHidden();
            Require(!host.Paused, "The visible tab did not resume.");
            Console.WriteLine("WebGPU HostLifecycle visibility=passed");
        }
        else
#endif
        host.Paused = false;
        if (batches is not null)
        {
            Require(host.Update(input) == DeterministicTickStatus.Pending && batches.InFlight == 1 &&
                ReferenceEquals(host.Render(), initial), "Poll passed the held GPU completion.");
            batches.Hold = false;
        }
        Require(await Drain(host) == DeterministicTickStatus.Completed, "Tick 1 failed.");
        var first = host.Render();
        VerifyFrame(first, 0, 2);
        Require(host.Poll() == DeterministicTickStatus.Completed && host.Captures == 2 &&
            ReferenceEquals(first, host.Render()), "Polling a completed tick republished it.");
        Require(host.Update(input) == DeterministicTickStatus.Pending && await Drain(host) == DeterministicTickStatus.Completed,
            "Tick 2 failed.");
        var second = host.Render();
        VerifyFrame(second, 0, 2, 4);
        VerifyFrame(first, 0, 2);
        VerifyFrame(initial, 0);
        Require(((IList<long>)first.Positions).IsReadOnly && ((IList<RenderEvent>)first.Events).IsReadOnly,
            "Render exposed mutable collections.");

        if (batches is not null) batches.Hold = true;
        Require(host.Update(new HostInput { Divisor = 1 }) == DeterministicTickStatus.Pending,
            "Reset scenario did not start a pending tick.");
        host.Paused = true;
        host.Reset();
        if (asyncKernel is not null)
            await asyncKernel.WaitForIdleAsync();
        if (batches is not null)
        {
            Require(batches.InFlight == 0 && batches.Discarded == 1, "Reset failed to drain the device batch.");
            batches.Hold = false;
        }
        Require(!host.Paused, "Reset did not restart the host clock.");
        VerifyFrame(host.Render(), 1);
        VerifyFrame(second, 0, 2, 4);
        Require(host.Update(new HostInput { Divisor = 2 }) == DeterministicTickStatus.Pending &&
            await Drain(host) == DeterministicTickStatus.Completed, "Reset could not restart the workload.");
        var beforeFault = host.Render();
        VerifyFrame(beforeFault, 1, 2);

        Require(host.Update(default) == DeterministicTickStatus.Pending && await Drain(host) == DeterministicTickStatus.Faulted,
            "An arithmetic fault did not follow the first successful slice.");
        // The row's integer remainder precedes its Fixed division in the fault evaluation order.
        Require(host.Fault is GaExecutionException { TickIndex: 2, Stage: SystemStage.Update, SystemOrder: 0, KernelOrder: 0,
            SourceOrderOrdinal: 64, SourceId: 64, SourceGeneration: 1,
            KernelFault: { Kind: GaFaultKind.DivideByZero, EvaluationOrdinal: 1 } }, $"Wrong canonical fault: {host.Fault}");
        var fault = (GaExecutionException)host.Fault!;
        submitted = batches?.Submitted ?? 0;
        Require(host.Update(input) == DeterministicTickStatus.Faulted && host.Poll() == DeterministicTickStatus.Faulted &&
            (batches?.Submitted ?? 0) == submitted && ReferenceEquals(beforeFault, host.Render()),
            "A fault published partial state or retried its tick.");
        VerifyLease(host);
        VerifyFrame(beforeFault, 1, 2);

        host.Reset();
        VerifyFrame(host.Render(), 2);
        Require(host.Update(new HostInput { Divisor = 2 }) == DeterministicTickStatus.Pending &&
            await Drain(host) == DeterministicTickStatus.Completed, "Host did not restart after discarding the faulted world.");
        var last = host.Render();
        VerifyFrame(last, 2, 2);
        if (batches is not null) batches.Hold = true;
        Require(host.Update(input) == DeterministicTickStatus.Pending, "Dispose scenario did not start a pending tick.");
        host.Dispose();
        if (asyncKernel is not null)
            await asyncKernel.WaitForIdleAsync();
        if (batches is not null)
            Require(batches.InFlight == 0 && batches.Discarded == 3, "Dispose failed to drain the device batch.");
        Require(host.Captures == 7 && ReferenceEquals(last, host.Render()), "Dispose published a partial frame.");
        VerifyFrame(last, 2, 2);
        (report ?? Console.WriteLine)($"{lane}: frames={host.Captures} exactCompletedEvents={4 * (HostSession.RowCount + 1)} " +
            $"canonicalFault=divideByZero/source{fault.SourceOrderOrdinal}/evaluation{fault.KernelFault.EvaluationOrdinal} " +
            "pendingOwnership=passed pauseResume=passed reset=passed dispose=passed immutableRender=passed");
        return fault;
    }

    static async Task<DeterministicTickStatus> Drain(HostSession host)
    {
        var published = host.Render();
        var timer = Stopwatch.StartNew();
#if !UNITY_2021_3_OR_NEWER
        var wait = new SpinWait();
#endif
        for (;;)
        {
            var state = host.Poll();
            if (state != DeterministicTickStatus.Pending)
                return state;
            Require(ReferenceEquals(published, host.Render()), "The host published a partial tick.");
            if (timer.Elapsed > TimeSpan.FromSeconds(20))
                throw new TimeoutException("The host stayed pending for 20 seconds.");
#if UNITY_2021_3_OR_NEWER
            await Task.Yield();
#else
            if (OperatingSystem.IsBrowser())
                await Task.Delay(1);
            else
                wait.SpinOnce();
#endif
        }
    }

    static void VerifyFrame(Frame frame, int generation, params int[] divisors)
    {
        Require(frame.Generation == generation && frame.Tick == divisors.Length && frame.Positions.Count == HostSession.RowCount,
            "Render frame identity or row count differs.");
        for (int row = 0; row < HostSession.RowCount; row++)
        {
            long position = row * 17L - 39;
            long step = (row % 7 - 3) * 4_294_967_296L;
            foreach (int divisor in divisors)
                position += row is 64 or 65 ? step / divisor : step;
            Require(frame.Positions[row] == position, $"Fixed raw output differs at row {row}.");
        }
        if (divisors.Length == 0)
        {
            Require(frame.Events.Count == 0, "Seed published tick events.");
            return;
        }
        Require(frame.Events.Count == HostSession.RowCount + 1, "Completed event count differs.");
        for (int row = 0; row < HostSession.RowCount; row++)
        {
            var item = frame.Events[row];
            Require(item.Value == row && item.Key is
                { Stage: SystemStage.Update, SystemOrder: 0, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Entity,
                    SourceGeneration: 1, LocalOrdinal: 0, DomainKey: null } &&
                item.Key.SourceId == row && item.Key.TickIndex == frame.Tick, $"Row event differs at {row}.");
        }
        var marker = frame.Events[^1];
        Require(marker.Value == -7 * divisors[^1] && marker.Key is
            { Stage: SystemStage.PostUpdate, SystemOrder: 1, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Job,
                SourceId: 0, SourceGeneration: 0, LocalOrdinal: 0, DomainKey: null } && marker.Key.TickIndex == frame.Tick,
            "Common post-update CPU event differs.");
    }

    static void VerifyLease(HostSession host)
    {
        try
        {
            host.ReadLiveWorld();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("The world permitted an external read while owned by the runner.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private readonly struct Observer : IDeterministicJobProcessor<HostInput>
    {
        public void Execute(DeterministicJob<HostInput> job) => job.ForJob().Emit(-7 * job.Input.Divisor);
    }
}
