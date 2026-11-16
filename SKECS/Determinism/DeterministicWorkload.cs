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

    /// <summary>Registers an ordered tick-local fold and returns its typed channel.</summary>
    public DeterministicReduction<T> AddReduction<T>(T seed, Func<T, T, T> combine) where T : unmanaged
    {
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

    /// <summary>Registers a single job with no entity rows. Query filters are disallowed.</summary>
    public DeterministicWorkload<TInput> AddJob<TProcessor>(
        SystemStage stage, DeterministicQuery access, TProcessor processor)
        where TProcessor : unmanaged, IDeterministicJobProcessor<TInput>
        => Add(stage, access, processor, runOnce: true);

    internal PreparedSystem<TInput>[] Prepare(World world)
    {
        var ordered = _systems.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            var stage = left.Stage.CompareTo(right.Stage);
            return stage != 0 ? stage : left.Order.CompareTo(right.Order);
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
        if (stage < SystemStage.PreUpdate || stage > SystemStage.PostUpdate)
            throw new ArgumentOutOfRangeException(nameof(stage));
        if (query is null) throw new ArgumentNullException(nameof(query));
        var rules = query.Snapshot();
        if (runOnce && Array.Exists(rules, static rule =>
                rule.Kind == ComponentRuleKind.Include || rule.Kind == ComponentRuleKind.Exclude))
            throw new ArgumentException("A standalone job has no entity filters.", nameof(query));

        _systems.Add(new SystemRegistration<TInput>(stage, _systems.Count, rules, runOnce, job =>
        {
            var copy = processor;
            copy.Execute(job);
        }));
        return this;
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
    private readonly ExecuteJob<TInput> _execute;

    public SystemRegistration(SystemStage stage, int order, ComponentRule[] rules, bool runOnce, ExecuteJob<TInput> execute)
    {
        Stage = stage;
        Order = order;
        _rules = rules;
        _runOnce = runOnce;
        _execute = execute;
    }

    public SystemStage Stage { get; }
    public int Order { get; }

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
        return new PreparedSystem<TInput>(Stage, Order, query, access, _execute);
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
    public PreparedSystem(SystemStage stage, int order, QueryCacheEntry? query,
        Dictionary<Type, PreparedAccess> access, ExecuteJob<TInput> execute)
    {
        Stage = stage;
        Order = order;
        Query = query;
        Access = access;
        Execute = execute;
    }

    public SystemStage Stage { get; }
    public int Order { get; }
    public QueryCacheEntry? Query { get; }
    public Dictionary<Type, PreparedAccess> Access { get; }
    public ExecuteJob<TInput> Execute { get; }
}
