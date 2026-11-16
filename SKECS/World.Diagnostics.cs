using System;

namespace SKECS;

public sealed partial class World
{
    private readonly DiagnosticBuffer? _diagnostics;

    /// <summary>Creates a world and records diagnostic events in the supplied buffer.</summary>
    public World(WorldOptions options, DiagnosticBuffer diagnostics) : this(options)
    {
        try
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            diagnostics.Attach(ChunkCapacity, UsesChurnControl, _adaptive != null);
            _diagnostics = diagnostics;
            RecordDiagnosticArchetype(_rootArchetype, 0);
            _churnPool?.AttachDiagnostics(diagnostics);
            if (_entityRecords.Capacity > 0) RecordDiagnosticEntityTable();
            if (_freeEntityIds.Capacity > 0) RecordDiagnosticFreeTable(_freeEntityIds.Capacity);
        }
        catch
        {
            DisposeCore();
            throw;
        }
    }

    internal DiagnosticBuffer? DiagnosticsBuffer => _diagnostics;

    private void RecordDiagnosticArchetype(Archetype archetype, int archetypeId)
    {
        var diagnostics = _diagnostics;
        if (diagnostics == null) return;

        archetype.DiagnosticId = archetypeId;
        long rowBytes = 2 * sizeof(int);
        diagnostics.DefineArchetype(archetypeId, archetype.ComponentTypes.Length, archetype.ChunkAllocationSize);
        foreach (var type in archetype.ComponentTypes)
        {
            rowBytes += type.Size;
            diagnostics.DefineColumn(archetypeId, type);
        }
        archetype.DiagnosticRowBytes = rowBytes;
    }

    private unsafe void RecordDiagnosticEntityTable()
    {
        var capacity = _entityRecords.Capacity;
        // sizeof gives the stride of the managed array, including the Archetype reference.
#pragma warning disable CS8500
        var payloadBytes = (long)capacity * sizeof(EntityRecord);
#pragma warning restore CS8500
        _diagnostics!.ResizeTable(DiagnosticTable.EntityRecords, 0, capacity, payloadBytes);
    }

    private void RecordDiagnosticFreeTable(int capacity)
    {
        _diagnostics?.ResizeTable(DiagnosticTable.FreeEntityIds, 0, capacity, (long)capacity * sizeof(int));
    }

    private void RecordDiagnosticCreate(Archetype archetype, int chunkIndex, int entityId)
    {
        _diagnostics?.CreateRow(archetype.DiagnosticId, chunkIndex, entityId);
    }

    private void RecordDiagnosticDestroy(int entityId, Archetype.RemovalResult removed)
    {
        var source = removed.Source!;
        _diagnostics?.DestroyRow(source.DiagnosticId, removed.MovedChunkIndex, entityId,
            removed.MovedEntityId >= 0 ? source.DiagnosticRowBytes : 0);
    }

    private void RecordDiagnosticTransition(Archetype.Transition transition, Archetype.RemovalResult removed, int targetChunk)
    {
        var source = removed.Source!;
        _diagnostics?.MoveRow(source.DiagnosticId, removed.MovedChunkIndex,
            transition.Destination.DiagnosticId, targetChunk, transition.DiagnosticCopiedBytes,
            removed.MovedEntityId >= 0 ? source.DiagnosticRowBytes : 0);
    }
}
