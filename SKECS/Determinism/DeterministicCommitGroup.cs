using System;
using System.Collections.Generic;

namespace SKECS;

/// <summary>Registers the ordered kernels of a deterministic commit group during its configuration callback.</summary>
public sealed class DeterministicCommitGroup<TInput> where TInput : unmanaged
{
    private readonly SystemStage _stage;
    private readonly int _systemOrder;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private List<SystemRegistration<TInput>>? _members = new();
    internal bool HasOrderedApply { get; private set; }

    internal DeterministicCommitGroup(SystemStage stage, int systemOrder, DeterministicDomainOrder? domainOrder)
    {
        _stage = stage;
        _systemOrder = systemOrder;
        DomainOrder = domainOrder;
    }

    public DeterministicDomainOrder? DomainOrder { get; }

    /// <summary>Registers an entity kernel that executes one job per group of up to 64 rows.</summary>
    public DeterministicCommitGroup<TInput> AddSystem<TProcessor>(DeterministicQuery query, TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => Add(query, processor, runOnce: false);

    /// <summary>Registers a per-job callback with immutable host bindings on the normal CPU worker path.</summary>
    public DeterministicCommitGroup<TInput> AddSystem(DeterministicQuery query, DeterministicJobExecution<TInput> execute)
    {
        var members = RequireOpen();
        members.Add(SystemRegistration<TInput>.ForJob(_stage, _systemOrder, members.Count, query, execute,
            runOnce: false, DomainOrder));
        return this;
    }

    /// <summary>Registers an entity kernel that submits consecutive ranges of logical jobs.</summary>
    public DeterministicCommitGroup<TInput> AddBatchSystem(DeterministicQuery query, IDeterministicBatchProcessor<TInput> processor)
    {
        var members = RequireOpen();
        members.Add(SystemRegistration<TInput>.ForBatch(_stage, _systemOrder, members.Count, query, processor, DomainOrder));
        return this;
    }

    /// <summary>Registers a preparation callback that can read row writes from preceding group kernels.</summary>
    public DeterministicCommitGroup<TInput> AddSnapshotPreparation(DeterministicQuery query,
        DeterministicSnapshotPreparation<TInput> prepare)
    {
        var members = RequireOpen();
        members.Add(SystemRegistration<TInput>.ForSnapshot(_stage, _systemOrder, members.Count, query, prepare, DomainOrder));
        return this;
    }

    /// <summary>Registers a single logical job. Its optional access declaration can contain component permissions.</summary>
    public DeterministicCommitGroup<TInput> AddJob<TProcessor>(DeterministicQuery? access, TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => Add(access ?? new DeterministicQuery(), processor, runOnce: true);

    /// <summary>Registers a single logical job without component access.</summary>
    public DeterministicCommitGroup<TInput> AddJob<TProcessor>(TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => AddJob(null, processor);

    /// <summary>Registers the final ordered apply stage after this group's producers.</summary>
    /// <remarks>It runs once after the sorted producer stream commits, even when there are no events.</remarks>
    public DeterministicCommitGroup<TInput> AddOrderedApply(DeterministicQuery access, DeterministicOrderedApply<TInput> apply)
    {
        var members = RequireOpen();
        if (members.Count == 0)
            throw new InvalidOperationException("Register at least one producer kernel before ordered apply.");
        members.Add(SystemRegistration<TInput>.ForOrderedApply(_stage, _systemOrder + 1, access, apply));
        HasOrderedApply = true;
        return this;
    }

    internal List<SystemRegistration<TInput>> Close()
    {
        var members = _members!;
        _members = null;
        return members;
    }

    private DeterministicCommitGroup<TInput> Add<TProcessor>(DeterministicQuery query, TProcessor processor, bool runOnce)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
    {
        var members = RequireOpen();
        members.Add(SystemRegistration<TInput>.ForJob(_stage, _systemOrder, members.Count, query, processor, runOnce, DomainOrder));
        return this;
    }

    private List<SystemRegistration<TInput>> RequireOpen()
    {
        if (Environment.CurrentManagedThreadId != _threadId || _members is null)
            throw new InvalidOperationException("A commit group builder is usable only during its configuration callback.");
        if (HasOrderedApply)
            throw new InvalidOperationException("Ordered apply must be the last member of its commit group.");
        return _members;
    }
}
