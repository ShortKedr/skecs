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
        writer.Write(program.Components.Count);
        foreach (var component in program.Components)
        {
            writer.Write(component.Id);
            writer.Write((byte)component.Access);
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
            default:
                throw new InvalidOperationException("Unknown GA IR statement.");
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
}
