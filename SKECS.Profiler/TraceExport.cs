using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;

namespace SKECS.Profiler;

internal static class TraceExport
{
    internal static void Write(Stream destination, ProfilerSession session, string scenario, string workloadId)
    {
        using var writer = XmlWriter.Create(destination);
        writer.WriteStartElement("skecs-profiler-trace");
        writer.WriteAttributeString("schemaVersion", session.Buffer.PackedPlacementEnabled ? "3" : session.Buffer.ChurnControlEnabled ? "2" : "1");
        writer.WriteAttributeString("scenario", scenario);
        writer.WriteAttributeString("workloadId", workloadId);
        writer.WriteAttributeString("runtime", RuntimeInformation.FrameworkDescription);
        writer.WriteAttributeString("os", RuntimeInformation.OSDescription);
        writer.WriteAttributeString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
        WriteNumber(writer, "stopwatchFrequency", Stopwatch.Frequency);
        WriteNumber(writer, "recordedEvents", session.Buffer.Count);
        WriteNumber(writer, "droppedEvents", session.Buffer.DroppedEvents);

        for (var i = 0; i < session.Buffer.Count; i++)
        {
            WriteEvent(writer, in session.Buffer[i]);
        }
        writer.WriteEndElement();
    }

    private static void WriteEvent(XmlWriter writer, in DiagnosticEvent entry)
    {
        writer.WriteStartElement("event");
        switch (entry.Kind)
        {
            case DiagnosticEventKind.WorldStarted:
                writer.WriteAttributeString("kind", "worldStarted");
                WriteNumber(writer, "chunkCapacity", entry.ChunkCapacity);
                break;
            case DiagnosticEventKind.ArchetypeDefined:
                writer.WriteAttributeString("kind", "archetypeDefined");
                WriteNumber(writer, "archetype", entry.ArchetypeId);
                WriteNumber(writer, "componentCount", entry.ComponentCount);
                WriteNumber(writer, "requestedNativeBytes", entry.RequestedNativeBytes);
                break;
            case DiagnosticEventKind.ArchetypeComponent:
                writer.WriteAttributeString("kind", "archetypeComponent");
                WriteNumber(writer, "archetype", entry.ArchetypeId);
                WriteNumber(writer, "componentId", entry.ComponentId);
                WriteComponentIdentity(writer, entry.ComponentType!);
                WriteNumber(writer, "size", entry.ComponentSize);
                WriteNumber(writer, "alignment", entry.ComponentAlignment);
                break;
            case DiagnosticEventKind.ChunkAllocated:
                writer.WriteAttributeString("kind", "chunkAllocated");
                WriteSlot(writer, entry);
                WriteNumber(writer, "nativeBytes", entry.NativeBytes);
                WriteNumber(writer, "pinnedBytes", entry.PinnedBytes);
                if (entry.BackingId != 0) WriteNumber(writer, "backing", entry.BackingId);
                break;
            case DiagnosticEventKind.ChunkReleased:
                writer.WriteAttributeString("kind", "chunkReleased");
                WriteSlot(writer, entry);
                break;
            case DiagnosticEventKind.ChunkPooled:
            case DiagnosticEventKind.ChunkReused:
                writer.WriteAttributeString("kind", entry.Kind == DiagnosticEventKind.ChunkPooled ? "chunkPooled" : "chunkReused");
                WriteSlot(writer, entry);
                WriteNumber(writer, "backing", entry.BackingId);
                break;
            case DiagnosticEventKind.PooledChunkAllocated:
                writer.WriteAttributeString("kind", "pooledChunkAllocated");
                WriteNumber(writer, "backing", entry.BackingId);
                WriteNumber(writer, "nativeBytes", entry.NativeBytes);
                WriteNumber(writer, "pinnedBytes", entry.PinnedBytes);
                break;
            case DiagnosticEventKind.PooledChunkReleased:
                writer.WriteAttributeString("kind", "pooledChunkReleased");
                WriteNumber(writer, "backing", entry.BackingId);
                break;
            case DiagnosticEventKind.RowCreated:
            case DiagnosticEventKind.RowDestroyed:
                writer.WriteAttributeString("kind", entry.Kind == DiagnosticEventKind.RowCreated ? "rowCreated" : "rowDestroyed");
                WriteSlot(writer, entry);
                WriteNumber(writer, "entity", entry.EntityId);
                if (entry.Kind == DiagnosticEventKind.RowDestroyed) WriteNumber(writer, "swapBytes", entry.SwapBytes);
                break;
            case DiagnosticEventKind.RowMoved:
                writer.WriteAttributeString("kind", "rowMoved");
                WriteSlot(writer, entry);
                WriteNumber(writer, "targetArchetype", entry.TargetArchetypeId);
                WriteNumber(writer, "targetChunk", entry.TargetChunkIndex);
                WriteNumber(writer, "copiedBytes", entry.CopiedBytes);
                WriteNumber(writer, "swapBytes", entry.SwapBytes);
                break;
            case DiagnosticEventKind.TableResized:
                writer.WriteAttributeString("kind", "tableResized");
                writer.WriteAttributeString("table", TableName(entry.Table));
                WriteNumber(writer, "tableId", entry.TableId);
                WriteNumber(writer, "capacity", entry.TableCapacity);
                WriteNumber(writer, "payloadBytes", entry.TablePayloadBytes);
                if (entry.ComponentType is Type componentType) WriteComponentIdentity(writer, componentType);
                break;
            case DiagnosticEventKind.PackedComponentDefined:
                writer.WriteAttributeString("kind", "packedComponentDefined");
                WritePackedType(writer, entry);
                WriteNumber(writer, "size", entry.ComponentSize);
                WriteNumber(writer, "alignment", entry.ComponentAlignment);
                WriteNumber(writer, "requestedNativeBytes", entry.RequestedNativeBytes);
                break;
            case DiagnosticEventKind.PackedPageAllocated:
            case DiagnosticEventKind.PackedPageReused:
            case DiagnosticEventKind.PackedPageReturned:
                writer.WriteAttributeString("kind", entry.Kind switch
                {
                    DiagnosticEventKind.PackedPageAllocated => "packedPageAllocated",
                    DiagnosticEventKind.PackedPageReused => "packedPageReused",
                    _ => "packedPageReturned"
                });
                WritePackedType(writer, entry);
                WriteNumber(writer, "page", entry.PackedPageIndex);
                WriteNumber(writer, "backing", entry.BackingId);
                WriteNumber(writer, "nativeBytes", entry.NativeBytes);
                WriteNumber(writer, "pinnedBytes", entry.PinnedBytes);
                break;
            case DiagnosticEventKind.PackedPageReleased:
                writer.WriteAttributeString("kind", "packedPageReleased");
                WritePackedType(writer, entry);
                WriteNumber(writer, "page", entry.PackedPageIndex);
                WriteNumber(writer, "backing", entry.BackingId);
                break;
            case DiagnosticEventKind.PackedRowsChanged:
                writer.WriteAttributeString("kind", "packedRowsChanged");
                WritePackedType(writer, entry);
                WriteNumber(writer, "count", entry.PackedRowCount);
                WriteNumber(writer, "entity", entry.EntityId);
                WriteNumber(writer, "delta", entry.PackedRowDelta);
                WriteNumber(writer, "swapBytes", entry.PackedSwapBytes);
                break;
            case DiagnosticEventKind.PackedIndexesMeasured:
                writer.WriteAttributeString("kind", "packedIndexesMeasured");
                WritePackedType(writer, entry);
                WriteNumber(writer, "indexBytes", entry.PackedIndexBytes);
                WriteNumber(writer, "presenceBytes", entry.PackedPresenceBytes);
                break;
            case DiagnosticEventKind.WorldDisposed:
                writer.WriteAttributeString("kind", "worldDisposed");
                break;
            case DiagnosticEventKind.FrameStarted:
            case DiagnosticEventKind.FrameCompleted:
                writer.WriteAttributeString("kind", entry.Kind == DiagnosticEventKind.FrameStarted ? "frameStarted" : "frameCompleted");
                WriteNumber(writer, "frame", entry.Frame);
                if (entry.Kind == DiagnosticEventKind.FrameCompleted) WriteNumber(writer, "elapsedTicks", entry.ElapsedTicks);
                break;
            case DiagnosticEventKind.Duration:
                writer.WriteAttributeString("kind", "duration");
                writer.WriteAttributeString("name", entry.MeasurementName);
                WriteNumber(writer, "elapsedTicks", entry.ElapsedTicks);
                break;
            default:
                throw new InvalidOperationException($"Unknown diagnostic event kind {entry.Kind}.");
        }
        writer.WriteEndElement();
    }

    private static string TableName(DiagnosticTable table) => table switch
    {
        DiagnosticTable.EntityRecords => "entityRecords",
        DiagnosticTable.FreeEntityIds => "freeEntityIds",
        DiagnosticTable.Accessor => "accessor",
        _ => throw new InvalidOperationException($"Unknown diagnostic table {table}.")
    };

    private static void WriteSlot(XmlWriter writer, in DiagnosticEvent entry)
    {
        WriteNumber(writer, "archetype", entry.ArchetypeId);
        WriteNumber(writer, "chunk", entry.ChunkIndex);
    }

    private static void WriteComponentIdentity(XmlWriter writer, Type type)
    {
        writer.WriteAttributeString("assembly", type.Assembly.GetName().Name);
        writer.WriteAttributeString("type", type.FullName ?? type.Name);
    }

    private static void WritePackedType(XmlWriter writer, in DiagnosticEvent entry)
    {
        WriteNumber(writer, "componentId", entry.PackedComponentId);
        WriteComponentIdentity(writer, entry.ComponentType!);
    }

    private static void WriteNumber(XmlWriter writer, string name, long value) =>
        writer.WriteAttributeString(name, XmlConvert.ToString(value));
}
