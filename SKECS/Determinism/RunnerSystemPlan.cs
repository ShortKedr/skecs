using System;
using System.Collections.Generic;
using SKECS.Numerics;

namespace SKECS;

internal sealed class RunnerSystemPlan<TInput> where TInput : unmanaged
{
    private readonly PreparedSystem<TInput> _system;
    private readonly RunnerRow[] _rows;
    private readonly JobBuffer?[] _outputs;

    public RunnerSystemPlan(PreparedSystem<TInput> system, long worldId)
    {
        _system = system;
        _rows = system.Query is { } query ? CaptureRows(query, worldId) : Array.Empty<RunnerRow>();
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
                    rows[index++] = new RunnerRow(entity, archetype, chunkIndex, row);
                }
            }
        }

        return rows;
    }

    public int JobCount => _outputs.Length;

    public void SortRows() => Array.Sort(_rows, RunnerRow.Compare);

    public void ExecuteJob(int jobId, long worldId, long tickIndex, TInput input, Fixed step,
        object ticketScope, PreparedReductions reductions)
    {
        var size = DeterministicRunner<TInput>.LogicalJobSize;
        var first = jobId * size;
        var rows = new ReadOnlySpan<RunnerRow>(_rows, first, Math.Min(size, _rows.Length - first));
        var buffer = new JobBuffer(jobId, rows.Length, worldId, tickIndex, _system.Stage, _system.Order,
            _system.Access, ticketScope, reductions);
        _system.Execute(new DeterministicJob<TInput>(rows, buffer, input, step));
        _outputs[jobId] = buffer;
    }

    public List<RunnerEmission>? CollectEmissions()
    {
        List<RunnerEmission>? merged = null;
        foreach (var output in _outputs)
        {
            if (output?.Emissions is { } emissions)
                (merged ??= new List<RunnerEmission>()).AddRange(emissions);
        }

        return merged;
    }
}
