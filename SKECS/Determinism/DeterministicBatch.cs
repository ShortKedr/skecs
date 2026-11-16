using System;
using SKECS.Numerics;

namespace SKECS;

/// <summary>Submits a consecutive range of canonical logical jobs to a batch executor.</summary>
public interface IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    /// <summary>Reads declared inputs and submits owned buffers. The batch cannot be retained.</summary>
    /// <remarks>If submission fails before returning a handle, the processor drains any work it began.</remarks>
    IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch);
}

/// <summary>An in-flight batch. The runner polls and applies it on its owner thread.</summary>
public interface IDeterministicBatchSubmission<TInput> : IDisposable where TInput : unmanaged
{
    /// <summary>Polls without blocking. A finished operation may have failed.</summary>
    bool IsCompleted { get; }

    /// <summary>Applies a completed batch or reports its failure. Called at most once.</summary>
    void Apply(DeterministicBatch<TInput> batch);

    /// <summary>Drains unfinished work without applying its results.</summary>
    void Discard();
}

/// <summary>Scoped access to a range of jobs in the current system plan.</summary>
public readonly ref struct DeterministicBatch<TInput> where TInput : unmanaged
{
    private readonly RunnerSystemPlan<TInput> _plan;
    private readonly long _worldId;
    private readonly object _ticketScope;
    private readonly PreparedReductions _reductions;
    private readonly bool _readOnly;

    internal DeterministicBatch(RunnerSystemPlan<TInput> plan, int firstJobId, int jobCount,
        long worldId, long tickIndex, TInput input, Fixed step, object ticketScope,
        PreparedReductions reductions, bool readOnly)
    {
        _plan = plan;
        FirstJobId = firstJobId;
        JobCount = jobCount;
        _worldId = worldId;
        TickIndex = tickIndex;
        Input = input;
        Step = step;
        _ticketScope = ticketScope;
        _reductions = reductions;
        _readOnly = readOnly;
    }

    public int FirstJobId { get; }
    public int JobCount { get; }
    public long TickIndex { get; }
    public TInput Input { get; }
    public Fixed Step { get; }
    public SystemStage Stage => _plan.Stage;
    public int SystemOrder => _plan.SystemOrder;

    public DeterministicBatchReadJob<TInput> GetReadJob(int jobId) => new(CreateJob(jobId));

    public DeterministicJob<TInput> GetJob(int jobId)
    {
        if (_readOnly)
            throw new InvalidOperationException("A batch can write and enqueue outputs only while applying its results.");
        return CreateJob(jobId);
    }

    private DeterministicJob<TInput> CreateJob(int jobId)
    {
        if (jobId < FirstJobId || jobId - FirstJobId >= JobCount)
            throw new ArgumentOutOfRangeException(nameof(jobId));
        return _plan.GetJob(jobId, _worldId, TickIndex, Input, Step, _ticketScope, _reductions);
    }
}

/// <summary>Read access to one batch job using the system's declared component access.</summary>
public readonly ref struct DeterministicBatchReadJob<TInput> where TInput : unmanaged
{
    private readonly DeterministicJob<TInput> _job;

    public DeterministicBatchReadJob(DeterministicJob<TInput> job) => _job = job;

    public long TickIndex => _job.TickIndex;
    public TInput Input => _job.Input;
    public Fixed Step => _job.Step;
    public int JobId => _job.JobId;
    public int Count => _job.Count;
    public Entity GetEntity(int index) => _job.GetEntity(index);
    public T Read<T>(int index) where T : unmanaged => _job.Read<T>(index);
    public bool TryRead<T>(int index, out T value) where T : unmanaged => _job.TryRead(index, out value);
}
