namespace SKECS.GA;

public enum GaScalar { Bool, Int32, UInt32, Fixed }
public enum GaFieldAccess { Read, ReadWrite }

public sealed record GaComponent(string Id, GaFieldAccess Access);
public sealed record GaField(string Component, string Path, GaScalar Type, GaFieldAccess Access)
{
    public string? ClrEnumType { get; init; }
}
public readonly record struct GaSourceSpan(string File, int Start, int Length);

internal enum GaUnaryOperator { Negate, BitwiseNot, Not }
internal enum GaBinaryOperator
{
    Add, Subtract, Multiply, Divide, Remainder,
    BitwiseAnd, BitwiseOr, ExclusiveOr, LeftShift, RightShift, UnsignedRightShift,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, AndAlso, OrElse
}

internal abstract record GaValue(GaScalar Type, GaSourceSpan Source);
internal sealed record GaConstant(GaScalar Type, ulong Bits, GaSourceSpan Source) : GaValue(Type, Source);
internal sealed record GaLoad(GaSlot Slot, GaSourceSpan Source) : GaValue(Slot.Type, Source);
internal sealed record GaCast(GaScalar Type, GaValue Operand, bool Checked, GaSourceSpan Source) : GaValue(Type, Source);
internal sealed record GaUnary(GaScalar Type, GaUnaryOperator Operator, GaValue Operand, bool Checked, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaBinary(GaScalar Type, GaBinaryOperator Operator, GaValue Left, GaValue Right, bool Checked, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaSelect(GaScalar Type, GaValue Condition, GaValue True, GaValue False, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaSnapshotRead(GaValue EntityId, GaValue Generation, GaValue Fallback, GaSourceSpan Source)
    : GaValue(GaScalar.Int32, Source);

internal readonly record struct GaSlot(bool IsField, int Index, GaScalar Type);
internal abstract record GaStatement;
internal sealed record GaBlock(IReadOnlyList<GaStatement> Statements) : GaStatement;
internal sealed record GaStore(GaSlot Slot, GaValue Value) : GaStatement;
internal sealed record GaEmit(GaValue Value) : GaStatement;
internal sealed record GaIf(GaValue Condition, GaStatement Then, GaStatement? Else) : GaStatement;
internal sealed record GaLoop(GaValue Condition, GaStatement Body, GaStatement Increment, int MaximumIterations)
    : GaStatement;

public sealed class GaProgram
{
    internal GaProgram(string systemId, string sourceDigest, GaComponent[] components, GaField[] fields, GaScalar[] locals, GaBlock body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        SystemId = systemId;
        SourceDigest = sourceDigest;
        Components = Array.AsReadOnly((GaComponent[])components.Clone());
        Fields = Array.AsReadOnly((GaField[])fields.Clone());
        Locals = Array.AsReadOnly((GaScalar[])locals.Clone());
        Body = body;
        Offsets = new int[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            Offsets[i] = WordCount;
            WordCount = checked(WordCount + (fields[i].Type == GaScalar.Fixed ? 2 : 1));
        }
        (UsesFixed, UsesSnapshot, EventCapacity) = GaVerifier.Verify(this);
    }

    // Initial word-based portable profile. Later profiles must explicitly extend this identity.
    public const int SubsetVersion = 3;
    public const int NumericVersion = SKECS.Numerics.Fixed.NumericContractVersion;
    public const int IrVersion = 3;
    public string SystemId { get; }
    public string SourceDigest { get; }
    public IReadOnlyList<GaComponent> Components { get; }
    public IReadOnlyList<GaField> Fields { get; }
    public int WordCount { get; }
    public bool UsesFixed { get; }
    /// <summary>Whether the program requires an immutable Int32 snapshot binding.</summary>
    public bool UsesSnapshot { get; }
    /// <summary>The verified maximum number of Int32 events emitted by one invocation.</summary>
    public int EventCapacity { get; }
    internal int[] Offsets { get; }
    internal IReadOnlyList<GaScalar> Locals { get; }
    internal GaBlock Body { get; }
}
