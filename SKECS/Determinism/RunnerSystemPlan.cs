using System;
using System.Collections.Generic;
using SKECS.Numerics;

namespace SKECS;

internal sealed class RunnerSystemPlan<TInput> where TInput : unmanaged
{
    private readonly PreparedSystem<TInput> _system;
    private readonly RunnerRow[] _rows;
    private readonly JobBuffer?[] _outputs;
    private RunnerSourceOrderKey[]? _sourceOrderKeys;

    public RunnerSystemPlan(PreparedSystem<TInput> system, long worldId)
    {
        _system = system;
        _rows = system.Query is { } query ? CaptureRows(query, worldId) : Array.Empty<RunnerRow>();
        if (system.SnapshotPreparation is not null)
        {
            _outputs = Array.Empty<JobBuffer?>();
            return;
        }
        var size = DeterministicRunner<TInput>.LogicalJobSize;
        var jobCount = system.Query is null ? 1 : (int)((_rows.Length + (long)size - 1) / size);
        _outputs = new JobBuffer?[jobCount];
    }

    private static RunnerRow[] CaptureRows(QueryCacheEntry query, long worldId)
    {
        var count = 0;
        foreach (var archetype in query.Matches)
            foreach (var chunk in archetype.Chunks)
                count = checked(count + chunk.Count);

        if (count == 0) return Array.Empty<RunnerRow>();
        var rows = new RunnerRow[count];
        var index = 0;
        foreach (var archetype in query.Matches)
        {
            for (var chunkIndex = 0; chunkIndex < archetype.Chunks.Count; chunkIndex++)
            {
                var chunk = archetype.Chunks[chunkIndex];
                for (var row = 0; row < chunk.Count; row++)
                {
                    var entity = new Entity(chunk.Entities[row], chunk.Versions[row], worldId);
                    rows[index++] = new RunnerRow(entity, archetype, chunk.SlotIndex, row);
                }
            }
        }

        return rows;
    }

    public int JobCount => _outputs.Length;
    public SystemStage Stage => _system.Stage;
    public int SystemOrder => _system.Order;
    public int KernelOrder => _system.KernelOrder;
    public DeterministicDomainOrder? DomainOrder => _system.DomainOrder;
    public IDeterministicBatchProcessor<TInput>? BatchProcessor => _system.BatchProcessor;
    public DeterministicSnapshotPreparation<TInput>? SnapshotPreparation => _system.SnapshotPreparation;

    public void SortRows()
    {
        Array.Sort(_rows, RunnerRow.Compare);
        if (_system.SelectSourceKey is not { } select || _rows.Length == 0)
            return;

        // Evaluate selectors in entity order as well so preparation failures are stable.
        var keys = new RunnerSourceOrderKey[_rows.Length];
        for (int row = 0; row < _rows.Length; row++)
            keys[row] = new RunnerSourceOrderKey(select(_rows[row]), row);
        Array.Sort(keys, _rows);
        _sourceOrderKeys = keys;
    }

    public DeterministicSnapshotSource<TInput> CreateSnapshotSource(long tickIndex, TInput input, Fixed step) =>
        new(_rows, _system.Access, tickIndex, Stage, SystemOrder, KernelOrder, input, step);

    public void ExecuteJob(int jobId, long worldId, long tickIndex, TInput input, Fixed step,
        object ticketScope, PreparedReductions reductions)
    {
        _system.Execute!(GetJob(jobId, worldId, tickIndex, input, step, ticketScope, reductions));
    }

    internal DeterministicJob<TInput> GetJob(int jobId, long worldId, long tickIndex, TInput input, Fixed step,
        object ticketScope, PreparedReductions reductions)
    {
        var size = DeterministicRunner<TInput>.LogicalJobSize;
        var first = jobId * size;
        var rows = new ReadOnlySpan<RunnerRow>(_rows, first, Math.Min(size, _rows.Length - first));
        var buffer = _outputs[jobId] ??= new JobBuffer(jobId, rows.Length, worldId, tickIndex, _system.Stage, _system.Order, KernelOrder,
            _system.Access, ticketScope, reductions, DomainOrder, _sourceOrderKeys);
        return new DeterministicJob<TInput>(rows, buffer, input, step);
    }

    public List<RunnerEmission>? CollectEmissions(List<RunnerEmission>? merged)
    {
        foreach (var output in _outputs)
        {
            if (output?.Emissions is { } emissions)
                (merged ??= new List<RunnerEmission>()).AddRange(emissions);
        }

        return merged;
    }
}

internal readonly struct RunnerSourceOrderKey : IComparable<RunnerSourceOrderKey>
{
    private readonly int _entityOrdinal;

    public RunnerSourceOrderKey(DeterministicDomainKey key, int entityOrdinal)
    {
        Key = key;
        _entityOrdinal = entityOrdinal;
    }

    public DeterministicDomainKey Key { get; }

    public int CompareTo(RunnerSourceOrderKey other)
    {
        int order = Key.CompareTo(other.Key);
        return order != 0 ? order : _entityOrdinal.CompareTo(other._entityOrdinal);
    }
}
