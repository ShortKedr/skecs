using System.Text;

namespace SKECS.GA;

internal static class GaIrDigest
{
    internal static string Schema(GaProgram program) => Digest(writer => WriteSchema(writer, program));

    internal static string Program(GaProgram program) => Digest(writer =>
    {
        writer.Write(GaProgram.IrVersion);
        writer.Write(GaProgram.SubsetVersion);
        writer.Write(GaProgram.NumericVersion);
        WriteSchema(writer, program);
        writer.Write(program.Locals.Count);
        foreach (var local in program.Locals)
            writer.Write((byte)local);
        writer.Write(program.TicketLocalCount);
        WriteStatement(writer, program.Body);
    });

    private static string Digest(Action<BinaryWriter> encode)
    {
        using var target = new MemoryStream();
        using var writer = new BinaryWriter(target, Encoding.UTF8, leaveOpen: true);
        encode(writer);
        return GaArtifactManifest.ComputeDigest(target.GetBuffer().AsSpan(0, checked((int)target.Length)));
    }

    private static void WriteSchema(BinaryWriter writer, GaProgram program)
    {
        writer.Write(program.UsesSnapshot);
        writer.Write(program.SnapshotLayout?.Schema ?? "");
        writer.Write(program.TickResourceType ?? "");
        writer.Write(program.BufferSchema ?? "");
        writer.Write(program.BufferRowWords);
        writer.Write(program.TickResourceFields.Count);
        foreach (var field in program.TickResourceFields)
        {
            writer.Write(field.Path);
            writer.Write((byte)field.Type);
            writer.Write(field.ClrEnumType ?? "");
        }
        writer.Write(program.UsesCommands);
        writer.Write(program.UsesDomainOrder);
        writer.Write(program.EventCapacity);
        writer.Write(program.RemovalComponents.Count);
        foreach (var component in program.RemovalComponents)
            writer.Write(component);
        writer.Write(program.AdditionPayloadWords);
        writer.Write(program.AdditionComponents.Count);
        foreach (var component in program.AdditionComponents)
            WritePayloadSchema(writer, component.Id, component.Fields);
        writer.Write(program.EventPayloadWords);
        writer.Write(program.EventTypes.Count);
        foreach (var eventType in program.EventTypes)
        {
            writer.Write(eventType.IsContribution);
            WritePayloadSchema(writer, eventType.Id, eventType.Fields);
        }
        writer.Write(program.Components.Count);
        foreach (var component in program.Components)
        {
            writer.Write(component.Id);
            writer.Write((byte)component.Access);
            writer.Write((byte)component.Origin);
        }
        writer.Write(program.Fields.Count);
        foreach (var field in program.Fields)
        {
            writer.Write(field.Component);
            writer.Write(field.Path);
            writer.Write((byte)field.Type);
            writer.Write((byte)field.Access);
            writer.Write(field.ClrEnumType ?? "");
        }
    }

    private static void WritePayloadSchema(BinaryWriter writer, string id, IReadOnlyList<GaAdditionField> fields)
    {
        writer.Write(id);
        writer.Write(fields.Count);
        foreach (var field in fields)
        {
            writer.Write(field.Path);
            writer.Write((byte)field.Type);
            writer.Write(field.ClrEnumType ?? "");
        }
    }

    private static void WriteStatement(BinaryWriter writer, GaStatement statement)
    {
        switch (statement)
        {
            case GaBlock block:
                writer.Write((byte)0);
                writer.Write(block.Statements.Count);
                foreach (var child in block.Statements)
                    WriteStatement(writer, child);
                break;
            case GaStore store:
                writer.Write((byte)1);
                WriteSlot(writer, store.Slot);
                WriteValue(writer, store.Value);
                break;
            case GaIf branch:
                writer.Write((byte)2);
                WriteValue(writer, branch.Condition);
                WriteStatement(writer, branch.Then);
                writer.Write(branch.Else != null);
                if (branch.Else != null)
                    WriteStatement(writer, branch.Else);
                break;
            case GaLoop loop:
                writer.Write((byte)3);
                writer.Write(loop.MaximumIterations);
                WriteValue(writer, loop.Condition);
                WriteStatement(writer, loop.Body);
                WriteStatement(writer, loop.Increment);
                break;
            case GaEmit emit:
                writer.Write((byte)4);
                WriteValue(writer, emit.Value);
                break;
            case GaEmitTyped emit:
                writer.Write((byte)14);
                writer.Write(emit.EventTypeIndex);
                WriteAdditionValues(writer, emit.Values);
                break;
            case GaDestroy destroy:
                writer.Write((byte)5);
                WriteValue(writer, destroy.EntityId);
                WriteValue(writer, destroy.Generation);
                break;
            case GaCreate create:
                writer.Write((byte)6);
                writer.Write(create.TicketSlot);
                break;
            case GaDestroyTicket destroy:
                writer.Write((byte)7);
                writer.Write(destroy.TicketSlot);
                break;
            case GaRemove remove:
                writer.Write((byte)8);
                writer.Write(remove.ComponentIndex);
                WriteValue(writer, remove.EntityId);
                WriteValue(writer, remove.Generation);
                break;
            case GaRemoveTicket remove:
                writer.Write((byte)9);
                writer.Write(remove.ComponentIndex);
                writer.Write(remove.TicketSlot);
                break;
            case GaAdd add:
                writer.Write((byte)10);
                writer.Write(add.ComponentIndex);
                WriteValue(writer, add.EntityId);
                WriteValue(writer, add.Generation);
                WriteAdditionValues(writer, add.Values);
                break;
            case GaAddTicket add:
                writer.Write((byte)11);
                writer.Write(add.ComponentIndex);
                writer.Write(add.TicketSlot);
                WriteAdditionValues(writer, add.Values);
                break;
            case GaSetDomainOrder order:
                writer.Write((byte)12);
                WriteValue(writer, order.Primary);
                WriteValue(writer, order.Secondary);
                break;
            case GaArrayStore store:
                writer.Write((byte)13);
                WriteArrayField(writer, store.Field);
                WriteValue(writer, store.Index);
                writer.Write(store.Previous.HasValue);
                if (store.Previous is { } previous)
                    WriteSlot(writer, previous);
                WriteValue(writer, store.Value);
                break;
            case GaArrayValueRead read:
                writer.Write((byte)15);
                WriteArrayValueFields(writer, read.Fields);
                WriteValue(writer, read.Index);
                break;
            case GaArrayValueStore store:
                writer.Write((byte)16);
                WriteArrayValueFields(writer, store.Fields);
                WriteValue(writer, store.Index);
                writer.Write(store.Previous != null);
                if (store.Previous != null)
                    foreach (var oldField in store.Previous)
                        WriteSlot(writer, oldField);
                WriteStatement(writer, store.Prepare);
                break;
            default:
                throw new InvalidOperationException("Unknown GA IR statement.");
        }
    }

    private static void WriteArrayValueFields(BinaryWriter writer, IReadOnlyList<GaArrayValueField> fields)
    {
        writer.Write(fields.Count);
        foreach (var field in fields)
        {
            WriteArrayField(writer, field.Field);
            WriteSlot(writer, field.Local);
            writer.Write(field.IsReadOnly);
        }
    }

    private static void WriteAdditionValues(BinaryWriter writer, IReadOnlyList<GaAdditionValue> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value.FieldIndex);
            WriteValue(writer, value.Value);
        }
    }

    private static void WriteValue(BinaryWriter writer, GaValue value)
    {
        writer.Write((byte)value.Type);
        switch (value)
        {
            case GaConstant constant:
                writer.Write((byte)0);
                writer.Write(constant.Bits);
                break;
            case GaLoad load:
                writer.Write((byte)1);
                WriteSlot(writer, load.Slot);
                break;
            case GaCast cast:
                writer.Write((byte)2);
                writer.Write(cast.Checked);
                WriteValue(writer, cast.Operand);
                break;
            case GaUnary unary:
                writer.Write((byte)3);
                writer.Write((byte)unary.Operator);
                writer.Write(unary.Checked);
                WriteValue(writer, unary.Operand);
                break;
            case GaBinary binary:
                writer.Write((byte)4);
                writer.Write((byte)binary.Operator);
                writer.Write(binary.Checked);
                WriteValue(writer, binary.Left);
                WriteValue(writer, binary.Right);
                break;
            case GaSelect select:
                writer.Write((byte)5);
                WriteValue(writer, select.Condition);
                WriteValue(writer, select.True);
                WriteValue(writer, select.False);
                break;
            case GaSnapshotRead lookup:
                writer.Write((byte)6);
                writer.Write(lookup.FieldIndex);
                WriteValue(writer, lookup.EntityId);
                WriteValue(writer, lookup.Generation);
                WriteValue(writer, lookup.Fallback);
                break;
            case GaArrayRead array:
                writer.Write((byte)7);
                WriteArrayField(writer, array.Field);
                WriteValue(writer, array.Index);
                break;
            case GaHasOwn has:
                writer.Write((byte)8);
                writer.Write(has.ComponentIndex);
                break;
            case GaTryRead read:
                writer.Write((byte)9);
                writer.Write(read.ComponentIndex);
                writer.Write(read.Copies.Count);
                foreach (var copy in read.Copies)
                {
                    WriteSlot(writer, copy.Local);
                    WriteSlot(writer, copy.Field);
                }
                break;
            case GaResourceRead read:
                writer.Write((byte)10);
                writer.Write(read.FieldIndex);
                break;
            case GaBufferCount:
                writer.Write((byte)11);
                break;
            case GaBufferRead read:
                writer.Write((byte)12);
                writer.Write(read.FieldIndex);
                WriteValue(writer, read.Index);
                break;
            case GaBoundValue bound:
                writer.Write((byte)13);
                WriteSlot(writer, bound.Slot);
                WriteValue(writer, bound.Value);
                WriteValue(writer, bound.Body);
                break;
            case GaStoredValue stored:
                writer.Write((byte)14);
                WriteSlot(writer, stored.Slot);
                WriteValue(writer, stored.Value);
                WriteValue(writer, stored.Body);
                break;
            case GaBlockValue block:
                writer.Write((byte)15);
                WriteStatement(writer, block.Body);
                WriteValue(writer, block.Result);
                break;
            default:
                throw new InvalidOperationException("Unknown GA IR value.");
        }
    }

    private static void WriteSlot(BinaryWriter writer, GaSlot slot)
    {
        writer.Write(slot.IsField);
        writer.Write(slot.Index);
        writer.Write((byte)slot.Type);
    }

    private static void WriteArrayField(BinaryWriter writer, GaArrayField field)
    {
        writer.Write(field.FirstField);
        writer.Write(field.Length);
        writer.Write((byte)field.Type);
    }
}
