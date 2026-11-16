using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using SKECS.Numerics;

namespace SKECS;

internal static class ParallelJobExecutor
{
    public static void Execute<TInput>(RunnerSystemPlan<TInput> plan, int firstJob, int jobCount, int workerCount,
        long worldId, long tickIndex, TInput input, Fixed step, object ticketScope, PreparedReductions reductions)
        where TInput : unmanaged
    {
        var end = firstJob + jobCount;
        if (workerCount == 1 || jobCount <= 1)
        {
            for (var jobId = firstJob; jobId < end; jobId++)
                plan.ExecuteJob(jobId, worldId, tickIndex, input, step, ticketScope, reductions);
            return;
        }

        ExecuteParallel(plan, firstJob, end, Math.Min(workerCount, jobCount), worldId, tickIndex,
            input, step, ticketScope, reductions);
    }

    private static void ExecuteParallel<TInput>(RunnerSystemPlan<TInput> plan, int firstJob, int end, int workerCount,
        long worldId, long tickIndex, TInput input, Fixed step, object ticketScope, PreparedReductions reductions)
        where TInput : unmanaged
    {
        var faultLock = new object();
        var firstFaultJob = end;
        Exception? firstFault = null;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = workerCount,
            TaskScheduler = TaskScheduler.Default,
        };

        Parallel.For(firstJob, end, options, jobId =>
        {
            try
            {
                plan.ExecuteJob(jobId, worldId, tickIndex, input, step, ticketScope, reductions);
            }
            catch (Exception fault)
            {
                // Fault selection follows logical job order after every dispatched job has drained.
                lock (faultLock)
                {
                    if (jobId < firstFaultJob)
                    {
                        firstFaultJob = jobId;
                        firstFault = fault;
                    }
                }
            }
        });

        if (firstFault != null) ExceptionDispatchInfo.Capture(firstFault).Throw();
    }
}
