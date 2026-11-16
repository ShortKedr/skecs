using System;
using System.Collections.Generic;
using System.Threading;

namespace SKECS;

/// <summary>A processor for one canonical logical job. The registered value is copied for each job.</summary>
public interface IDeterministicJobProcessor<TInput> where TInput : unmanaged
{
    void Execute(DeterministicJob<TInput> job);
}

/// <summary>Entity filters and component access for a deterministic system.</summary>
/// <remarks>Filters and access are independent. Include a component with With when every row
/// must contain it. Write or ReadWrite also grants permission to enqueue commands for the type.</remarks>
public sealed class DeterministicQuery
{
    private readonly List<ComponentRule> _rules = new();

    public DeterministicQuery With<T>() where T : unmanaged => Add<T>(ComponentRuleKind.Include);

    public DeterministicQuery Without<T>() where T : unmanaged => Add<T>(ComponentRuleKind.Exclude);

    public DeterministicQuery Read<T>() where T : unmanaged => Add<T>(ComponentRuleKind.Read);

    public DeterministicQuery Write<T>() where T : unmanaged => Add<T>(ComponentRuleKind.Write);

    public DeterministicQuery ReadWrite<T>() where T : unmanaged => Add<T>(ComponentRuleKind.ReadWrite);

    /// <summary>Specifies row order for an entity kernel without changing its query membership or access rules.</summary>
    public DeterministicQuery OrderBy(DeterministicSourceOrder order)
    {
        SourceOrder = order ?? throw new ArgumentNullException(nameof(order));
        return this;
    }

    internal DeterministicSourceOrder? SourceOrder { get; private set; }

    internal ComponentRule[] Snapshot() => _rules.ToArray();

    private DeterministicQuery Add<T>(ComponentRuleKind kind) where T : unmanaged
    {
        _rules.Add(new ComponentRule(typeof(T), kind, static world => world.GetComponentType<T>().Id));
        return this;
    }
}

/// <summary>Builds an ordered set of systems. Each runner takes a snapshot of its configuration.</summary>
public sealed class DeterministicWorkload<TInput> where TInput : unmanaged
{
    private readonly List<SystemRegistration<TInput>> _systems = new();
    private readonly List<object> _reductions = new();
    private readonly long _id = Interlocked.Increment(ref WorkloadIds.Next);
    private int _systemCount;
    private bool _configuringGroup;

    /// <summary>Registers an ordered tick-local fold and returns its typed channel.</summary>
    public DeterministicReduction<T> AddReduction<T>(T seed, Func<T, T, T> combine) where T : unmanaged
    {
        RequireOutsideGroup();
        if (combine is null) throw new ArgumentNullException(nameof(combine));
        var channel = new DeterministicReduction<T>(_id, _reductions.Count, seed);
        _reductions.Add(new ReductionDefinition<T>(channel, combine));
        return channel;
    }

    /// <summary>Registers a system that runs one job per group of up to 64 matching rows.</summary>
    public DeterministicWorkload<TInput> AddSystem<TProcessor>(
        SystemStage stage, DeterministicQuery query, TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => Add(stage, query, processor, runOnce: false);

    /// <summary>Registers an entity system that submits consecutive ranges of logical jobs.</summary>
    /// <remarks>The registered processor belongs to its runner. Use independent processors
    /// when building workloads for runners that execute concurrently.</remarks>
    public DeterministicWorkload<TInput> AddBatchSystem(
        SystemStage stage, DeterministicQuery query, IDeterministicBatchProcessor<TInput> processor)
    {
        RequireOutsideGroup();
        _systems.Add(SystemRegistration<TInput>.ForBatch(stage, _systemCount, 0, query, processor));
        _systemCount++;
        return this;
    }

    /// <summary>Registers a single job with no entity rows. Query filters are disallowed.</summary>
    public DeterministicWorkload<TInput> AddJob<TProcessor>(
        SystemStage stage, DeterministicQuery access, TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => Add(stage, access, processor, runOnce: true);

    /// <summary>Registers a preparation callback that reads the current system's rows once per tick.</summary>
    /// <remarks>The callback runs on the runner thread, including when its query has no matches.</remarks>
    public DeterministicWorkload<TInput> AddSnapshotPreparation(SystemStage stage, DeterministicQuery query,
        DeterministicSnapshotPreparation<TInput> prepare)
    {
        RequireOutsideGroup();
        _systems.Add(SystemRegistration<TInput>.ForSnapshot(stage, _systemCount, 0, query, prepare));
        _systemCount++;
        return this;
    }

    /// <summary>Registers an ordered group of kernels with a shared commit barrier.</summary>
    /// <remarks>Later kernels see row writes from earlier kernels. Commands, events and reductions
    /// from the group become visible at its final barrier. Configure members through the supplied builder.</remarks>
    public DeterministicWorkload<TInput> AddCommitGroup(SystemStage stage, Action<DeterministicCommitGroup<TInput>> configure)
        => AddCommitGroupCore(stage, null, configure);

    /// <summary>Registers a commit group whose outputs are sorted by their declared domain keys.</summary>
    public DeterministicWorkload<TInput> AddCommitGroup(SystemStage stage, DeterministicDomainOrder order,
        Action<DeterministicCommitGroup<TInput>> configure)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        return AddCommitGroupCore(stage, order, configure);
    }

    private DeterministicWorkload<TInput> AddCommitGroupCore(SystemStage stage, DeterministicDomainOrder? domainOrder,
        Action<DeterministicCommitGroup<TInput>> configure)
    {
        RequireOutsideGroup();
        SystemRegistration<TInput>.RequireStage(stage);
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var group = new DeterministicCommitGroup<TInput>(stage, _systemCount, domainOrder);
        List<SystemRegistration<TInput>> members;
        _configuringGroup = true;
        try
        {
            configure(group);
        }
        finally
        {
            members = group.Close();
            _configuringGroup = false;
        }
        if (members.Count == 0) throw new ArgumentException("A commit group needs at least one kernel.", nameof(configure));
        _systems.AddRange(members);
        _systemCount++;
        return this;
    }

    internal PreparedSystem<TInput>[] Prepare(World world)
    {
        RequireOutsideGroup();
        var ordered = _systems.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            var stage = left.Stage.CompareTo(right.Stage);
            if (stage != 0) return stage;
            var system = left.Order.CompareTo(right.Order);
            return system != 0 ? system : left.KernelOrder.CompareTo(right.KernelOrder);
        });
        var prepared = new PreparedSystem<TInput>[ordered.Length];
        for (var i = 0; i < prepared.Length; i++)
            prepared[i] = ordered[i].Prepare(world);
        return prepared;
    }

    internal PreparedReductions PrepareReductions() => new(_id, _reductions.ToArray());

    private DeterministicWorkload<TInput> Add<TProcessor>(
        SystemStage stage, DeterministicQuery query, TProcessor processor, bool runOnce)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
    {
        RequireOutsideGroup();
        _systems.Add(SystemRegistration<TInput>.ForJob(stage, _systemCount, 0, query, processor, runOnce));
        _systemCount++;
        return this;
    }

    private void RequireOutsideGroup()
    {
        if (_configuringGroup)
            throw new InvalidOperationException("Configure commit group kernels through the supplied builder.");
    }
}

internal static class WorkloadIds
{
    internal static long Next;
}

[Flags]
internal enum ComponentRuleKind
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
    Include = 4,
    Exclude = 8,
}

internal readonly struct ComponentRule
{
    public ComponentRule(Type type, ComponentRuleKind kind, Func<World, int> register)
    {
        Type = type;
        Kind = kind;
        Register = register;
    }

    public Type Type { get; }
    public ComponentRuleKind Kind { get; }
    public Func<World, int> Register { get; }
}

internal delegate void ExecuteJob<TInput>(DeterministicJob<TInput> job) where TInput : unmanaged;

internal sealed class SystemRegistration<TInput> where TInput : unmanaged
{
    private readonly ComponentRule[] _rules;
    private readonly bool _runOnce;
    private readonly ExecuteJob<TInput>? _execute;
    private readonly IDeterministicBatchProcessor<TInput>? _batchProcessor;
    private readonly DeterministicSnapshotPreparation<TInput>? _snapshotPreparation;
    private readonly DeterministicSourceOrder? _sourceOrder;

    private SystemRegistration(SystemStage stage, int order, int kernelOrder, ComponentRule[] rules, bool runOnce,
        ExecuteJob<TInput>? execute, IDeterministicBatchProcessor<TInput>? batchProcessor = null,
        DeterministicSnapshotPreparation<TInput>? snapshotPreparation = null, DeterministicDomainOrder? domainOrder = null,
        DeterministicSourceOrder? sourceOrder = null)
    {
        Stage = stage;
        Order = order;
        KernelOrder = kernelOrder;
        DomainOrder = domainOrder;
        _rules = rules;
        _runOnce = runOnce;
        _execute = execute;
        _batchProcessor = batchProcessor;
        _snapshotPreparation = snapshotPreparation;
        _sourceOrder = sourceOrder;
    }

    public SystemStage Stage { get; }
    public int Order { get; }
    public int KernelOrder { get; }
    public DeterministicDomainOrder? DomainOrder { get; }

    public static SystemRegistration<TInput> ForJob<TProcessor>(SystemStage stage, int order, int kernelOrder,
        DeterministicQuery query, TProcessor processor, bool runOnce, DeterministicDomainOrder? domainOrder = null)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
    {
        var rules = GetRules(stage, query);
        if (runOnce && Array.Exists(rules, static rule =>
                rule.Kind == ComponentRuleKind.Include || rule.Kind == ComponentRuleKind.Exclude))
            throw new ArgumentException("A standalone job has no entity filters.", nameof(query));
        if (runOnce && query.SourceOrder is not null)
            throw new ArgumentException("A standalone job has no entity source order.", nameof(query));

        return new SystemRegistration<TInput>(stage, order, kernelOrder, rules, runOnce, job =>
        {
            var copy = processor;
            copy.Execute(job);
        }, domainOrder: domainOrder, sourceOrder: query.SourceOrder);
    }

    public static SystemRegistration<TInput> ForBatch(SystemStage stage, int order, int kernelOrder,
        DeterministicQuery query, IDeterministicBatchProcessor<TInput> processor, DeterministicDomainOrder? domainOrder = null)
    {
        if (processor is null) throw new ArgumentNullException(nameof(processor));
        return new SystemRegistration<TInput>(stage, order, kernelOrder, GetRules(stage, query),
            runOnce: false, execute: null, batchProcessor: processor, domainOrder: domainOrder, sourceOrder: query.SourceOrder);
    }

    public static SystemRegistration<TInput> ForSnapshot(SystemStage stage, int order, int kernelOrder,
        DeterministicQuery query, DeterministicSnapshotPreparation<TInput> prepare, DeterministicDomainOrder? domainOrder = null)
    {
        if (prepare is null) throw new ArgumentNullException(nameof(prepare));
        var rules = GetRules(stage, query);
        if (query.SourceOrder is not null)
            throw new ArgumentException("Snapshot preparation always reads rows in canonical entity order.", nameof(query));
        return new SystemRegistration<TInput>(stage, order, kernelOrder, rules,
            runOnce: false, execute: null, snapshotPreparation: prepare, domainOrder: domainOrder);
    }

    internal static void RequireStage(SystemStage stage)
    {
        if (stage < SystemStage.PreUpdate || stage > SystemStage.PostUpdate)
            throw new ArgumentOutOfRangeException(nameof(stage));
    }

    private static ComponentRule[] GetRules(SystemStage stage, DeterministicQuery query)
    {
        RequireStage(stage);
        if (query is null) throw new ArgumentNullException(nameof(query));
        return query.Snapshot();
    }

    public PreparedSystem<TInput> Prepare(World world)
    {
        var include = ComponentSetKey.Empty;
        var exclude = ComponentSetKey.Empty;
        var access = new Dictionary<Type, PreparedAccess>();
        foreach (var rule in _rules)
        {
            var typeId = rule.Register(world);
            switch (rule.Kind)
            {
                case ComponentRuleKind.Include:
                    include = include.Add(typeId);
                    break;
                case ComponentRuleKind.Exclude:
                    exclude = exclude.Add(typeId);
                    break;
                default:
                    access.TryGetValue(rule.Type, out var previous);
                    access[rule.Type] = new PreparedAccess(typeId, previous.Kind | rule.Kind);
                    break;
            }
        }

        var query = _runOnce ? null : world.GetOrCreateQueryCache(new QueryCacheKey(include, exclude));
        return new PreparedSystem<TInput>(Stage, Order, KernelOrder, query, access, _execute, _batchProcessor,
            _snapshotPreparation, DomainOrder, _sourceOrder?.Bind(world));
    }
}

internal readonly struct PreparedAccess
{
    public PreparedAccess(int typeId, ComponentRuleKind kind)
    {
        TypeId = typeId;
        Kind = kind;
    }

    public int TypeId { get; }
    public ComponentRuleKind Kind { get; }

    public static int Require<T>(Dictionary<Type, PreparedAccess> access, ComponentRuleKind kind) where T : unmanaged
    {
        if (access.TryGetValue(typeof(T), out var declaration) && (declaration.Kind & kind) == kind)
            return declaration.TypeId;
        throw new InvalidOperationException($"The system did not declare {kind} access to {typeof(T).Name}.");
    }
}

internal sealed class PreparedSystem<TInput> where TInput : unmanaged
{
    public PreparedSystem(SystemStage stage, int order, int kernelOrder, QueryCacheEntry? query,
        Dictionary<Type, PreparedAccess> access, ExecuteJob<TInput>? execute,
        IDeterministicBatchProcessor<TInput>? batchProcessor, DeterministicSnapshotPreparation<TInput>? snapshotPreparation,
        DeterministicDomainOrder? domainOrder, Func<RunnerRow, DeterministicDomainKey>? selectSourceKey)
    {
        Stage = stage;
        Order = order;
        KernelOrder = kernelOrder;
        DomainOrder = domainOrder;
        Query = query;
        Access = access;
        Execute = execute;
        BatchProcessor = batchProcessor;
        SnapshotPreparation = snapshotPreparation;
        SelectSourceKey = selectSourceKey;
    }

    public SystemStage Stage { get; }
    public int Order { get; }
    public int KernelOrder { get; }
    public DeterministicDomainOrder? DomainOrder { get; }
    public QueryCacheEntry? Query { get; }
    public Dictionary<Type, PreparedAccess> Access { get; }
    public ExecuteJob<TInput>? Execute { get; }
    public IDeterministicBatchProcessor<TInput>? BatchProcessor { get; }
    public DeterministicSnapshotPreparation<TInput>? SnapshotPreparation { get; }
    public Func<RunnerRow, DeterministicDomainKey>? SelectSourceKey { get; }
}
