using System;
using System.Collections.Generic;
using System.Diagnostics;
using SKECS.Numerics;

namespace SKECS;

public enum DeterministicTickStatus
{
    Idle,
    Pending,
    Completed,
    Faulted,
}

/// <summary>Executes one logical tick across one or more Advance calls.</summary>
/// <remarks>Call on one thread at a time. Hold on to the last completed presentation snapshot
/// while a tick owns the world. Previously borrowed references and pointers must not be used
/// during ownership. A failed or aborted tick keeps the world owned until it is discarded.</remarks>
public sealed class DeterministicRunner<TInput> : IDisposable where TInput : unmanaged
{
    public const int LogicalJobSize = 64;

    private readonly World _world;
    private readonly PreparedSystem<TInput>[] _systems;
    private readonly PreparedReductions _definitions;
    private readonly int _workerCount;
    private World.RunnerLease? _lease;
    private RunnerSystemPlan<TInput>? _plan;
    private List<RunnerEmission>? _groupEmissions;
    private object? _ticketScope;
    private List<DeterministicEvent>? _events;
    private Dictionary<int, object>? _reductions;
    private long _tickIndex;
    private TInput _input;
    private Fixed _step;
    private int _kernelIndex;
    private int _nextJob;
    private int _pendingCount;
    private IDeterministicBatchSubmission<TInput>? _pending;
    private bool _advancing;
    private bool _disposed;

    public DeterministicRunner(World world, DeterministicWorkload<TInput> workload)
        : this(world, workload, workerCount: 1)
    {
    }

    /// <summary>Uses up to the given positive number of workers for a batch of logical jobs.</summary>
    /// <remarks>Jobs can execute in any order. The runner waits for the full parallel batch and
    /// reports the fault of the lowest failing logical job before publishing any results.</remarks>
    public DeterministicRunner(World world, DeterministicWorkload<TInput> workload, int workerCount)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (workload is null) throw new ArgumentNullException(nameof(workload));
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        _workerCount = workerCount;
        _world.ThrowIfDisposed();
        _systems = workload.Prepare(world);
        _definitions = workload.PrepareReductions();
    }

    public DeterministicTickStatus Status { get; private set; }

    public DeterministicTickResult? LastCompleted { get; private set; }

    public Exception? Fault { get; private set; }

    /// <summary>Reserves the world and copies the tick index, inputs and simulation step by value.</summary>
    /// <remarks>Tick indices must be nonnegative and strictly increasing across this runner's ticks.</remarks>
    public void BeginTick(long tickIndex, in TInput input, Fixed step)
    {
        EnsureAvailable();
        if (_lease != null) throw new InvalidOperationException("The preceding tick still owns the world.");
        if (tickIndex < 0 || (LastCompleted != null && tickIndex <= LastCompleted.TickIndex))
            throw new ArgumentOutOfRangeException(nameof(tickIndex));

        // Allocate first so an exception cannot strand a lease during setup.
        var events = new List<DeterministicEvent>();
        var reductions = new Dictionary<int, object>();
        var ticketScope = new object();
        var lease = _world.AcquireRunner();
        _lease = lease;
        _events = events;
        _reductions = reductions;
        _ticketScope = ticketScope;
        _tickIndex = tickIndex;
        _input = input;
        _step = step;
        Status = DeterministicTickStatus.Pending;
    }

    /// <summary>Submits up to the given positive number of new logical jobs.</summary>
    /// <remarks>Plan preparation, snapshot callbacks, sorting and system barriers can also take time, including
    /// after the last budgeted job. This budget does not bound wall-clock duration.</remarks>
    public DeterministicTickStatus Advance(int logicalJobBudget)
    {
        EnsureAvailable();
        if (logicalJobBudget <= 0) throw new ArgumentOutOfRangeException(nameof(logicalJobBudget));
        if (Status == DeterministicTickStatus.Idle) throw new InvalidOperationException("Begin a tick before advancing it.");
        if (Status != DeterministicTickStatus.Pending) return Status;

        _advancing = true;
        try
        {
            var trace = RunnerTrace.Log.IsEnabled() ? RunnerTrace.Log : null;
            if (!TryApplyBatch())
                return Status;
            while (_kernelIndex < _systems.Length)
            {
                if (_plan is null)
                {
                    var started = trace == null ? 0 : Stopwatch.GetTimestamp();
                    _plan = new RunnerSystemPlan<TInput>(_systems[_kernelIndex], _world.Id);
                    trace?.Finish("plan", started);
                    started = trace == null ? 0 : Stopwatch.GetTimestamp();
                    _plan.SortRows();
                    trace?.Finish("row-sort", started);
                    if (_plan.SnapshotPreparation is { } prepare)
                    {
                        started = trace == null ? 0 : Stopwatch.GetTimestamp();
                        prepare(_plan.CreateSnapshotSource(_tickIndex, _input, _step));
                        trace?.Finish("snapshot", started);
                    }
                }

                var batch = Math.Min(logicalJobBudget, _plan.JobCount - _nextJob);
                if (batch != 0)
                {
                    var started = trace == null ? 0 : Stopwatch.GetTimestamp();
                    if (_plan.BatchProcessor is { } processor)
                    {
                        _pendingCount = batch;
                        _pending = processor.Begin(CreateBatch(readOnly: true)) ??
                            throw new InvalidOperationException("A deterministic batch processor returned no submission.");
                    }
                    else
                    {
                        ParallelJobExecutor.Execute(_plan, _nextJob, batch, _workerCount, _world.Id, _tickIndex,
                            _input, _step, _ticketScope!, _definitions);
                        _nextJob += batch;
                    }
                    trace?.Finish("jobs", started);
                    logicalJobBudget -= batch;
                    if (!TryApplyBatch())
                        return Status;
                }

                if (_nextJob != _plan.JobCount) return Status;
                CompleteKernel(trace);
                _plan = null;
                _kernelIndex++;
                _nextJob = 0;
            }

            var completed = new DeterministicTickResult(_tickIndex, _events!.ToArray(), _definitions, _reductions!);
            _lease!.Release();
            _lease = null;
            ClearTick();
            LastCompleted = completed;
            Status = DeterministicTickStatus.Completed;
        }
        catch (Exception fault)
        {
            SetFault(fault);
        }
        finally
        {
            _advancing = false;
        }

        return Status;
    }

    /// <summary>Faults a pending tick and retains ownership of its partially updated world.</summary>
    public void Abort()
    {
        EnsureAvailable();
        if (Status != DeterministicTickStatus.Pending) throw new InvalidOperationException("There is no pending tick to abort.");
        _advancing = true;
        try
        {
            SetFault(new OperationCanceledException("The deterministic tick was aborted."));
        }
        finally
        {
            _advancing = false;
        }
    }

    /// <summary>Destroys the owned world and closes the runner after a pending or faulted tick.</summary>
    public void DiscardWorld()
    {
        EnsureAvailable();
        if (_lease is null) throw new InvalidOperationException("There is no owned world to discard.");
        Dispose();
    }

    /// <summary>Closes the runner. A pending or faulted tick also destroys its owned world.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        EnsureAvailable();
        _advancing = true;
        try
        {
            if (Status == DeterministicTickStatus.Pending)
                SetFault(new OperationCanceledException("The owned world was discarded."));
            _lease?.Discard();
            _lease = null;
            _disposed = true;
        }
        finally
        {
            _advancing = false;
        }
    }

    private void CompleteKernel(RunnerTrace? trace)
    {
        var started = trace == null ? 0 : Stopwatch.GetTimestamp();
        var plan = _plan!;
        var emissions = plan.CollectEmissions(_groupEmissions);
        var next = _kernelIndex + 1;
        if (next < _systems.Length && _systems[next].Order == plan.SystemOrder)
        {
            _groupEmissions = emissions;
            trace?.Finish("merge-sort", started);
            return;
        }
        _groupEmissions = null;
        emissions?.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        trace?.Finish("merge-sort", started);
        if (emissions is null) return;
        started = trace == null ? 0 : Stopwatch.GetTimestamp();
        var barrier = new RunnerBarrier(_lease!, _events!, _reductions!);
        foreach (var emission in emissions)
            emission.Apply(barrier);
        trace?.Finish("commit", started);
    }

    private DeterministicBatch<TInput> CreateBatch(bool readOnly) =>
        new(_plan!, _nextJob, _pendingCount, _world.Id, _tickIndex, _input, _step, _ticketScope!, _definitions, readOnly);

    private bool TryApplyBatch()
    {
        if (_pending is not { } submission)
            return true;
        if (!submission.IsCompleted)
            return false;
        submission.Apply(CreateBatch(readOnly: false));
        _nextJob += _pendingCount;
        _pendingCount = 0;
        _pending = null;
        submission.Dispose();
        return true;
    }

    private void ClearTick()
    {
        _plan = null;
        _groupEmissions = null;
        _events = null;
        _reductions = null;
        _ticketScope = null;
        _input = default;
        _kernelIndex = 0;
        _nextJob = 0;
        _pendingCount = 0;
    }

    private void SetFault(Exception fault)
    {
        Status = DeterministicTickStatus.Faulted;
        if (_pending is { } submission)
        {
            _pending = null;
            try
            {
                submission.Discard();
            }
            catch (Exception discardFault)
            {
                fault = new AggregateException("The deterministic batch failed while draining.", fault, discardFault);
            }
            try
            {
                submission.Dispose();
            }
            catch (Exception disposeFault)
            {
                fault = new AggregateException("The deterministic batch failed while releasing its resources.", fault, disposeFault);
            }
        }
        Fault = fault;
        ClearTick();
    }

    private void EnsureAvailable()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DeterministicRunner<TInput>));
        if (_advancing) throw new InvalidOperationException("The runner cannot be entered from a job or barrier.");
    }
}
