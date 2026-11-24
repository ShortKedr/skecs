namespace SKECS.GA;

public enum GaScalar { Bool, Int32, UInt32, Fixed }
public enum GaFieldAccess { Read, ReadWrite }

public enum GaComponentOrigin { Query, Own, Optional }

public sealed record GaComponent(string Id, GaFieldAccess Access)
{
    public GaComponentOrigin Origin { get; init; }
    public GaSourceSpan OwnSource { get; init; }
}
public sealed record GaField(string Component, string Path, GaScalar Type, GaFieldAccess Access)
{
    public string? ClrEnumType { get; init; }
}
public readonly record struct GaSourceSpan(string File, int Start, int Length);
public sealed record GaTickResourceField(string Path, GaScalar Type, string? ClrEnumType);
public sealed record GaBufferField(string Path, GaScalar Type, string? ClrEnumType);

public sealed record GaAdditionField(string Path, GaScalar Type, string? ClrEnumType);

/// <summary>The portable field schema of one component addition.</summary>
public sealed class GaAdditionComponent
{
    internal GaAdditionComponent(string id, GaAdditionField[] fields)
    {
        Id = id;
        Fields = Array.AsReadOnly((GaAdditionField[])fields.Clone());
        Offsets = new int[fields.Length];
        for (int field = 0; field < fields.Length; field++)
        {
            Offsets[field] = WordCount;
            WordCount = checked(WordCount + (fields[field].Type == GaScalar.Fixed ? 2 : 1));
        }
    }

    public string Id { get; }
    public IReadOnlyList<GaAdditionField> Fields { get; }
    public int WordCount { get; }
    internal int[] Offsets { get; }
}

/// <summary>The packed portable payload schema of a typed event or reduction leaf.</summary>
public sealed class GaEventType
{
    internal GaEventType(string id, GaAdditionField[] fields, bool isContribution = false)
    {
        Id = id;
        IsContribution = isContribution;
        Fields = Array.AsReadOnly((GaAdditionField[])fields.Clone());
        Offsets = new int[fields.Length];
        for (int field = 0; field < fields.Length; field++)
        {
            Offsets[field] = WordCount;
            WordCount = checked(WordCount + (fields[field].Type == GaScalar.Fixed ? 2 : 1));
        }
    }

    public string Id { get; }
    public bool IsContribution { get; }
    public IReadOnlyList<GaAdditionField> Fields { get; }
    public int WordCount { get; }
    internal int[] Offsets { get; }
}

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
internal sealed record GaBoundValue(GaSlot Slot, GaValue Value, GaValue Body, GaSourceSpan Source)
    : GaValue(Body.Type, Source);
internal sealed record GaStoredValue(GaSlot Slot, GaValue Value, GaValue Body, GaSourceSpan Source)
    : GaValue(Body.Type, Source);
internal sealed record GaBlockValue(GaStatement Body, GaValue Result, GaSourceSpan Source)
    : GaValue(Result.Type, Source);
internal sealed record GaCast(GaScalar Type, GaValue Operand, bool Checked, GaSourceSpan Source) : GaValue(Type, Source);
internal sealed record GaUnary(GaScalar Type, GaUnaryOperator Operator, GaValue Operand, bool Checked, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaBinary(GaScalar Type, GaBinaryOperator Operator, GaValue Left, GaValue Right, bool Checked, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaSelect(GaScalar Type, GaValue Condition, GaValue True, GaValue False, GaSourceSpan Source)
    : GaValue(Type, Source);
internal sealed record GaSnapshotRead(GaValue EntityId, GaValue Generation, GaValue Fallback, GaSourceSpan Source)
    : GaValue(Fallback.Type, Source)
{
    internal int FieldIndex { get; init; } = -1;
}
internal sealed record GaArrayRead(GaArrayField Field, GaValue Index, GaSourceSpan Source)
    : GaValue(Field.Type, Source);
internal sealed record GaHasOwn(int ComponentIndex, GaSourceSpan Source) : GaValue(GaScalar.Bool, Source);
internal sealed record GaTryRead(int ComponentIndex, IReadOnlyList<GaOptionalFieldCopy> Copies, GaSourceSpan Source)
    : GaValue(GaScalar.Bool, Source);
internal sealed record GaResourceRead(int FieldIndex, GaScalar Type, GaSourceSpan Source) : GaValue(Type, Source);
internal sealed record GaBufferRead(int FieldIndex, GaValue Index, GaScalar Type, GaSourceSpan Source) : GaValue(Type, Source);
internal sealed record GaBufferCount(GaSourceSpan Source) : GaValue(GaScalar.Int32, Source);

internal readonly record struct GaSlot(bool IsField, int Index, GaScalar Type);
internal readonly record struct GaArrayField(int FirstField, int Length, GaScalar Type);
internal readonly record struct GaOptionalFieldCopy(GaSlot Local, GaSlot Field);
internal abstract record GaStatement;
internal sealed record GaBlock(IReadOnlyList<GaStatement> Statements) : GaStatement;
internal sealed record GaStore(GaSlot Slot, GaValue Value) : GaStatement;
internal sealed record GaArrayStore(GaArrayField Field, GaValue Index, GaSlot? Previous, GaValue Value, GaSourceSpan Source)
    : GaStatement;
internal readonly record struct GaArrayValueField(GaArrayField Field, GaSlot Local, bool IsReadOnly = false);
internal sealed record GaArrayValueRead(IReadOnlyList<GaArrayValueField> Fields, GaValue Index, GaSourceSpan Source)
    : GaStatement;
internal sealed record GaArrayValueStore(IReadOnlyList<GaArrayValueField> Fields, GaValue Index,
    IReadOnlyList<GaSlot>? Previous, GaBlock Prepare, GaSourceSpan Source) : GaStatement;
internal sealed record GaEmit(GaValue Value) : GaStatement;
internal sealed record GaEmitTyped(int EventTypeIndex, IReadOnlyList<GaAdditionValue> Values) : GaStatement;
internal sealed record GaSetDomainOrder(GaValue Primary, GaValue Secondary) : GaStatement;
internal sealed record GaDestroy(GaValue EntityId, GaValue Generation) : GaStatement;
internal sealed record GaCreate(int TicketSlot) : GaStatement;
internal sealed record GaDestroyTicket(int TicketSlot) : GaStatement;
internal sealed record GaRemove(int ComponentIndex, GaValue EntityId, GaValue Generation) : GaStatement;
internal sealed record GaRemoveTicket(int ComponentIndex, int TicketSlot) : GaStatement;
internal sealed record GaAdditionValue(int FieldIndex, GaValue Value);
internal sealed record GaAdd(int ComponentIndex, GaValue EntityId, GaValue Generation,
    IReadOnlyList<GaAdditionValue> Values) : GaStatement;
internal sealed record GaAddTicket(int ComponentIndex, int TicketSlot, IReadOnlyList<GaAdditionValue> Values) : GaStatement;
internal sealed record GaIf(GaValue Condition, GaStatement Then, GaStatement? Else) : GaStatement;
internal sealed record GaLoop(GaValue Condition, GaStatement Body, GaStatement Increment, int MaximumIterations)
    : GaStatement;

public sealed class GaProgram
{
    internal GaProgram(string systemId, string sourceDigest, GaComponent[] components, GaField[] fields,
        GaScalar[] locals, GaBlock body, int ticketLocalCount, string[] removalComponents,
        GaAdditionComponent[] additionComponents, GaSnapshotLayout? snapshotLayout = null,
        string? tickResourceType = null, GaTickResourceField[]? tickResourceFields = null,
        string? bufferType = null, GaBufferField[]? bufferFields = null, GaEventType[]? eventTypes = null)
    {
        if (systemId is null)
            throw new ArgumentNullException(nameof(systemId));
        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(systemId));
        SystemId = systemId;
        SourceDigest = sourceDigest;
        Components = Array.AsReadOnly((GaComponent[])components.Clone());
        Fields = Array.AsReadOnly((GaField[])fields.Clone());
        Locals = Array.AsReadOnly((GaScalar[])locals.Clone());
        TicketLocalCount = ticketLocalCount;
        RemovalComponents = Array.AsReadOnly((string[])removalComponents.Clone());
        AdditionComponents = Array.AsReadOnly((GaAdditionComponent[])additionComponents.Clone());
        AdditionPayloadWords = additionComponents.Length == 0 ? 0 : additionComponents.Max(component => component.WordCount);
        EventTypes = Array.AsReadOnly(eventTypes?.ToArray() ?? []);
        EventPayloadWords = EventTypes.Count == 0 ? 0 : EventTypes.Max(type => type.WordCount);
        ReductionTypeCount = EventTypes.Count(type => type.IsContribution);
        SnapshotLayout = snapshotLayout;
        TickResourceType = tickResourceType;
        TickResourceFields = Array.AsReadOnly(tickResourceFields?.ToArray() ?? []);
        TickResourceOffsets = new int[TickResourceFields.Count];
        for (int i = 0; i < TickResourceFields.Count; i++)
        {
            TickResourceOffsets[i] = TickResourceWords;
            TickResourceWords = checked(TickResourceWords + (TickResourceFields[i].Type == GaScalar.Fixed ? 2 : 1));
        }
        BufferType = bufferType;
        BufferFields = Array.AsReadOnly(bufferFields?.ToArray() ?? []);
        BufferOffsets = new int[BufferFields.Count];
        for (int i = 0; i < BufferFields.Count; i++)
        {
            BufferOffsets[i] = BufferRowWords;
            BufferRowWords = checked(BufferRowWords + (BufferFields[i].Type == GaScalar.Fixed ? 2 : 1));
        }
        Body = body;
        Offsets = new int[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            Offsets[i] = WordCount;
            WordCount = checked(WordCount + (fields[i].Type == GaScalar.Fixed ? 2 : 1));
        }
        PresenceOffsets = new int[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            PresenceOffsets[i] = components[i].Origin == GaComponentOrigin.Query ? -1 : WordCount;
            if (PresenceOffsets[i] >= 0)
                WordCount = checked(WordCount + 1);
        }
        (UsesFixed, UsesSnapshot, UsesCommands, UsesDomainOrder, EventCapacity) = GaVerifier.Verify(this);
        if (BufferType is not null)
            BufferSchema = ComputeBufferSchema(BufferType, BufferFields);
    }

    // Initial word-based portable profile. Later profiles must explicitly extend this identity.
    public const int SubsetVersion = 15;
    public const int NumericVersion = SKECS.Numerics.Fixed.NumericContractVersion;
    public const int IrVersion = 15;
    public string SystemId { get; }
    public string SourceDigest { get; }
    public IReadOnlyList<GaComponent> Components { get; }
    public IReadOnlyList<GaField> Fields { get; }
    /// <summary>The exact runner input type declared by the kernel, if any.</summary>
    public string? TickResourceType { get; }
    public IReadOnlyList<GaTickResourceField> TickResourceFields { get; }
    public int TickResourceWords { get; }
    public string? BufferType { get; }
    public string? BufferSchema { get; }
    public IReadOnlyList<GaBufferField> BufferFields { get; }
    public int BufferRowWords { get; }
    public int WordCount { get; }
    public bool UsesFixed { get; }
    /// <summary>Whether the program requires an immutable snapshot binding.</summary>
    public bool UsesSnapshot { get; }
    /// <summary>The declared snapshot layout, or null for legacy and snapshot-free programs.</summary>
    public GaSnapshotLayout? SnapshotLayout { get; }
    /// <summary>Whether the program can stage structural commands for the commit barrier.</summary>
    public bool UsesCommands { get; }
    /// <summary>Whether the program can set a domain key for its outputs.</summary>
    public bool UsesDomainOrder { get; }
    /// <summary>The number of distinct ticket locals in the verified system.</summary>
    public int TicketLocalCount { get; }
    /// <summary>The component identities referenced by removal commands in source traversal order.</summary>
    public IReadOnlyList<string> RemovalComponents { get; }
    /// <summary>The portable schemas referenced by component additions in source traversal order.</summary>
    public IReadOnlyList<GaAdditionComponent> AdditionComponents { get; }
    /// <summary>The maximum word count of an addition payload before padding.</summary>
    public int AdditionPayloadWords { get; }
    /// <summary>The schemas of typed outputs in source traversal order.</summary>
    public IReadOnlyList<GaEventType> EventTypes { get; }
    public int EventPayloadWords { get; }
    public int ReductionTypeCount { get; }
    public int OutputPayloadWords => Math.Max(AdditionPayloadWords, EventPayloadWords);
    /// <summary>The verified maximum number of events and commands staged by one invocation.</summary>
    public int EventCapacity { get; }
    internal int[] Offsets { get; }
    internal int[] PresenceOffsets { get; }
    internal int[] TickResourceOffsets { get; }
    internal int[] BufferOffsets { get; }
    internal IReadOnlyList<GaScalar> Locals { get; }
    internal GaBlock Body { get; }

    private static string ComputeBufferSchema(string type, IReadOnlyList<GaBufferField> fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1); // Packed read-buffer schema version.
        writer.Write(type);
        writer.Write(fields.Count);
        foreach (var field in fields)
        {
            writer.Write(field.Path);
            writer.Write((byte)field.Type);
            writer.Write(field.ClrEnumType ?? "");
        }
        return GaArtifactManifest.ComputeDigest(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
