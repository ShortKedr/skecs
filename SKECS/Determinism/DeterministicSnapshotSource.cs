using System;
using System.Collections.Generic;
using SKECS.Numerics;

namespace SKECS;

/// <summary>Builds immutable input from a snapshot preparation kernel's declared rows.</summary>
public delegate void DeterministicSnapshotPreparation<TInput>(DeterministicSnapshotSource<TInput> source)
    where TInput : unmanaged;

/// <summary>Scoped read access to a snapshot preparation kernel's rows in canonical entity order.</summary>
public readonly ref struct DeterministicSnapshotSource<TInput> where TInput : unmanaged
{
    private readonly ReadOnlySpan<RunnerRow> _rows;
    private readonly Dictionary<Type, PreparedAccess> _access;

    internal DeterministicSnapshotSource(ReadOnlySpan<RunnerRow> rows, Dictionary<Type, PreparedAccess> access,
        long tickIndex, SystemStage stage, int systemOrder, int kernelOrder, TInput input, Fixed step)
    {
        _rows = rows;
        _access = access;
        TickIndex = tickIndex;
        Stage = stage;
        SystemOrder = systemOrder;
        KernelOrder = kernelOrder;
        Input = input;
        Step = step;
    }

    public int Count => _rows.Length;
    public long TickIndex { get; }
    public SystemStage Stage { get; }
    public int SystemOrder { get; }
    public int KernelOrder { get; }
    public TInput Input { get; }
    public Fixed Step { get; }

    public Entity GetEntity(int index) => _rows[index].Entity;

    public T Read<T>(int index) where T : unmanaged
    {
        var typeId = PreparedAccess.Require<T>(_access, ComponentRuleKind.Read);
        return _rows[index].Read<T>(typeId);
    }

    public bool TryRead<T>(int index, out T value) where T : unmanaged
    {
        var typeId = PreparedAccess.Require<T>(_access, ComponentRuleKind.Read);
        var row = _rows[index];
        if (!row.Archetype.HasComponent(typeId))
        {
            value = default;
            return false;
        }

        value = row.Read<T>(typeId);
        return true;
    }
}
