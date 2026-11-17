using System.Runtime.InteropServices;
using SKECS.Numerics;

namespace SKECS.GA;

public enum GaFaultKind : uint { Overflow = 1, DivideByZero, IterationLimit, IndexOutOfRange, MissingOwnComponent }

public readonly record struct GaKernelFault(GaFaultKind Kind, ulong EvaluationOrdinal, GaSourceSpan Source);

public static class GaInterpreter
{
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row)
    {
        ArgumentNullException.ThrowIfNull(program);
        RequireUnpackedSupport(program);
        if (program.UsesCommands)
            throw new InvalidOperationException("Use the Execute overload with ordered output storage for this program.");
        if (program.EventCapacity != 0)
            throw new InvalidOperationException("Use the Execute overload with event storage for this program.");
        return Execute(program, row, Span<int>.Empty, out _);
    }

    /// <summary>Executes one packed row. Row and events are published only on success.</summary>
    /// <param name="program">The verified GA program.</param>
    /// <param name="row">Packed component words.</param>
    /// <param name="events">Separate storage with room for <see cref="GaProgram.EventCapacity"/> events.</param>
    /// <param name="eventCount">The number of published events. Zero on fault.</param>
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, Span<int> events, out int eventCount) =>
        Execute(program, row, events, out eventCount, null);

    /// <summary>Executes one row against the provided immutable snapshot binding.</summary>
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, Span<int> events, out int eventCount,
        GaSnapshotPacket? snapshot) => ExecuteCore(program, row, events, out eventCount, snapshot, null, default, packed: false);

    /// <summary>Executes one row against the immutable snapshot and packed tick input.</summary>
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, Span<int> events, out int eventCount,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources) =>
        ExecuteCore(program, row, events, out eventCount, snapshot, null, default, packed: false, tickResources);

    /// <summary>Executes one row with its frozen tick input, snapshot and read buffer.</summary>
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, Span<int> events, out int eventCount,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources, GaBufferPacket? buffer) =>
        ExecuteCore(program, row, events, out eventCount, snapshot, null, default, packed: false, tickResources, buffer);

    /// <summary>Executes one row and appends its ordered events and commands only on success.</summary>
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, ICollection<GaOutputRecord> outputs,
        GaSnapshotPacket? snapshot = null)
        => Execute(program, row, outputs, snapshot, default);

    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, ICollection<GaOutputRecord> outputs,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources)
        => Execute(program, row, outputs, snapshot, tickResources, null);

    public static GaKernelFault? Execute(GaProgram program, Span<uint> row, ICollection<GaOutputRecord> outputs,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources, GaBufferPacket? buffer)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        return ExecuteCore(program, row, Span<int>.Empty, out _, snapshot, outputs, default, packed: false, tickResources, buffer);
    }

    /// <summary>Executes one row and publishes packed records on success.</summary>
    /// <remarks>Each record contains three header words, the program's output payload words,
    /// and three domain-key words when the program uses domain order.</remarks>
    public static GaKernelFault? ExecutePackedOutputs(GaProgram program, Span<uint> row, Span<uint> records, out int count,
        GaSnapshotPacket? snapshot = null) => ExecuteCore(program, row, default, out count, snapshot, null, records, packed: true);

    public static GaKernelFault? ExecutePackedOutputs(GaProgram program, Span<uint> row, Span<uint> records, out int count,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources) =>
        ExecuteCore(program, row, default, out count, snapshot, null, records, packed: true, tickResources);

    public static GaKernelFault? ExecutePackedOutputs(GaProgram program, Span<uint> row, Span<uint> records, out int count,
        GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources, GaBufferPacket? buffer) =>
        ExecuteCore(program, row, default, out count, snapshot, null, records, packed: true, tickResources, buffer);

    private static void RequireUnpackedSupport(GaProgram program)
    {
        if (program.AdditionComponents.Count != 0)
            throw new InvalidOperationException("Use ExecutePackedOutputs for programs that add components.");
        if (program.EventTypes.Count != 0)
            throw new InvalidOperationException("Use ExecutePackedOutputs for programs with typed events or reduction leaves.");
        if (program.UsesDomainOrder)
            throw new InvalidOperationException("Use ExecutePackedOutputs for programs that set domain order.");
    }

    private static GaKernelFault? ExecuteCore(GaProgram program, Span<uint> row, Span<int> events, out int eventCount,
        GaSnapshotPacket? snapshot, ICollection<GaOutputRecord>? outputs, Span<uint> records, bool packed,
        ReadOnlySpan<uint> tickResources = default, GaBufferPacket? buffer = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        eventCount = 0;
        if (!packed)
        {
            RequireUnpackedSupport(program);
            if (program.UsesCommands && outputs is null)
                throw new InvalidOperationException("Use the Execute overload with ordered output storage for this program.");
        }
        if (program.UsesSnapshot)
        {
            if (snapshot is null)
                throw new InvalidOperationException("The GA program requires a snapshot binding.");
            snapshot.RequireSchema(program.SnapshotLayout?.Schema);
        }
        if (program.BufferSchema is { } schema)
        {
            if (buffer is null)
                throw new InvalidOperationException("The GA program requires an immutable read buffer.");
            buffer.RequireSchema(schema, program.BufferRowWords);
        }
        else if (buffer is not null)
            throw new ArgumentException("The GA program does not declare a read buffer.", nameof(buffer));
        if (row.Length != program.WordCount)
            throw new ArgumentException("The row length must match the packed GA schema.", nameof(row));
        if (tickResources.Length != program.TickResourceWords)
            throw new ArgumentException("The tick resource length must match the packed GA schema.", nameof(tickResources));
        if (tickResources.Overlaps(row))
            throw new ArgumentException("Tick resource storage must not overlap the component row.", nameof(tickResources));
        for (int i = 0; i < program.TickResourceFields.Count; i++)
            if (program.TickResourceFields[i].Type == GaScalar.Bool && tickResources[program.TickResourceOffsets[i]] > 1)
                throw new ArgumentException("Tick resource booleans must be encoded as 0 or 1.", nameof(tickResources));
        int stride = packed
            ? GaOutputRecord.Words + program.OutputPayloadWords + (program.UsesDomainOrder ? GaOutputRecord.DomainWordCount : 0)
            : 0;
        int packedWordCount = checked(stride * program.EventCapacity);
        if (packed)
        {
            if (records.Length < packedWordCount)
                throw new ArgumentException("The destination must hold the verified packed output capacity.", nameof(records));
            if (row.Overlaps(records[..packedWordCount]))
                throw new ArgumentException("Output storage must not overlap the component row.", nameof(records));
            if (tickResources.Overlaps(records[..packedWordCount]))
                throw new ArgumentException("Output storage must not overlap tick resources.", nameof(records));
        }
        else if (outputs is null)
        {
            if (events.Length < program.EventCapacity)
                throw new ArgumentException("The destination must hold the verified event capacity.", nameof(events));
            if (MemoryMarshal.Cast<uint, int>(row).Overlaps(events[..program.EventCapacity]))
                throw new ArgumentException("Event storage must not overlap the component row.", nameof(events));
            if (tickResources.Overlaps(MemoryMarshal.Cast<int, uint>(events[..program.EventCapacity])))
                throw new ArgumentException("Event storage must not overlap tick resources.", nameof(events));
        }
        for (int i = 0; i < program.Fields.Count; i++)
            if (program.Fields[i].Type == GaScalar.Bool && row[program.Offsets[i]] > 1)
                throw new ArgumentException($"Field {i} is not a canonical boolean.", nameof(row));
        foreach (int offset in program.PresenceOffsets)
            if (offset >= 0 && row[offset] > 1)
                throw new ArgumentException("Component presence must be encoded as 0 or 1.", nameof(row));
        for (int i = 0; i < program.Components.Count; i++)
        {
            var component = program.Components[i];
            if (component.Origin == GaComponentOrigin.Own && row[program.PresenceOffsets[i]] == 0)
                return new GaKernelFault(GaFaultKind.MissingOwnComponent, 0, component.OwnSource);
        }

        Span<uint> next = row.Length <= 128 ? stackalloc uint[row.Length] : new uint[row.Length];
        Span<ulong> locals = program.Locals.Count <= 64
            ? stackalloc ulong[program.Locals.Count]
            : new ulong[program.Locals.Count];
        Span<int> tickets = program.TicketLocalCount <= 64
            ? stackalloc int[program.TicketLocalCount]
            : new int[program.TicketLocalCount];
        int eventCapacity = !packed && outputs is null ? program.EventCapacity : 0;
        int outputCapacity = outputs is null ? 0 : program.EventCapacity;
        Span<int> nextEvents = eventCapacity <= 128 ? stackalloc int[eventCapacity] : new int[eventCapacity];
        Span<GaOutputRecord> nextOutputs = outputCapacity <= 128
            ? stackalloc GaOutputRecord[outputCapacity]
            : new GaOutputRecord[outputCapacity];
        Span<uint> nextRecords = packedWordCount <= 384
            ? stackalloc uint[packedWordCount]
            : new uint[packedWordCount];
        row.CopyTo(next);
        var executor = new Executor(next, locals, tickets, nextEvents, nextOutputs, nextRecords, stride, program, snapshot, tickResources, buffer);
        executor.Run(program.Body);
        if (executor.Fault == null)
        {
            if (packed)
                nextRecords[..(stride * executor.OutputCount)].CopyTo(records);
            else if (outputs is null)
                nextEvents[..executor.OutputCount].CopyTo(events);
            else
                foreach (var output in nextOutputs[..executor.OutputCount])
                    outputs.Add(output);
            next.CopyTo(row);
            eventCount = executor.OutputCount;
        }
        return executor.Fault;
    }

    private ref struct Executor
    {
        private readonly Span<uint> _row;
        private readonly Span<ulong> _locals;
        private readonly Span<int> _tickets;
        private readonly Span<int> _events;
        private readonly Span<GaOutputRecord> _outputs;
        private readonly Span<uint> _records;
        private readonly int _stride;
        private readonly int[] _offsets;
        private readonly int[] _presenceOffsets;
        private readonly int[] _tickResourceOffsets;
        private readonly ReadOnlySpan<uint> _tickResources;
        private readonly int[] _bufferOffsets;
        private readonly GaBufferPacket? _buffer;
        private readonly IReadOnlyList<GaAdditionComponent> _additions;
        private readonly IReadOnlyList<GaEventType> _eventTypes;
        private readonly GaSnapshotPacket? _snapshot;
        private readonly bool _usesDomainOrder;
        private bool _hasDomainKey;
        private int _domainPrimary;
        private int _domainSecondary;
        private ulong _evaluationOrdinal;
        internal GaKernelFault? Fault { get; private set; }
        internal int OutputCount { get; private set; }

        internal Executor(Span<uint> row, Span<ulong> locals, Span<int> tickets, Span<int> events, Span<GaOutputRecord> outputs,
            Span<uint> records, int stride, GaProgram program, GaSnapshotPacket? snapshot, ReadOnlySpan<uint> tickResources,
            GaBufferPacket? buffer)
        {
            _row = row;
            _locals = locals;
            _tickets = tickets;
            _events = events;
            _outputs = outputs;
            _records = records;
            _stride = stride;
            _offsets = program.Offsets;
            _presenceOffsets = program.PresenceOffsets;
            _tickResourceOffsets = program.TickResourceOffsets;
            _tickResources = tickResources;
            _bufferOffsets = program.BufferOffsets;
            _buffer = buffer;
            _additions = program.AdditionComponents;
            _eventTypes = program.EventTypes;
            _snapshot = snapshot;
            _usesDomainOrder = program.UsesDomainOrder;
        }

        private ulong Load(GaSlot slot)
        {
            if (!slot.IsField)
                return _locals[slot.Index];
            return ReadWords(_row, _offsets[slot.Index], slot.Type);
        }

        private static ulong ReadWords(ReadOnlySpan<uint> words, int offset, GaScalar type) =>
            type == GaScalar.Fixed ? words[offset] | (ulong)words[offset + 1] << 32 : words[offset];

        private void Store(GaSlot slot, ulong value)
        {
            if (!slot.IsField)
            {
                _locals[slot.Index] = value;
                return;
            }
            int offset = _offsets[slot.Index];
            _row[offset] = unchecked((uint)value);
            if (slot.Type == GaScalar.Fixed)
                _row[offset + 1] = unchecked((uint)(value >> 32));
        }

        private bool TryIndex(GaArrayField field, GaValue index, GaSourceSpan source, out GaSlot slot)
        {
            slot = default;
            uint element = unchecked((uint)Read(index));
            if (Fault != null)
                return false;
            ulong ordinal = _evaluationOrdinal++;
            if (element >= (uint)field.Length)
            {
                Fault = new GaKernelFault(GaFaultKind.IndexOutOfRange, ordinal, source);
                return false;
            }
            slot = new GaSlot(true, field.FirstField + (int)element, field.Type);
            return true;
        }

        internal void Run(GaStatement statement)
        {
            if (Fault != null)
                return;
            switch (statement)
            {
                case GaBlock block:
                    foreach (var child in block.Statements)
                        Run(child);
                    break;
                case GaStore store:
                    Store(store.Slot, Read(store.Value));
                    break;
                case GaArrayStore indexed:
                    if (TryIndex(indexed.Field, indexed.Index, indexed.Source, out var target))
                    {
                        if (indexed.Previous is { } previous)
                            Store(previous, Load(target));
                        ulong updated = Read(indexed.Value);
                        if (Fault == null)
                            Store(target, updated);
                    }
                    break;
                case GaEmit emit:
                    int value = unchecked((int)Read(emit.Value));
                    if (Fault == null)
                    {
                        if (_outputs.IsEmpty && _stride == 0)
                            _events[OutputCount++] = value;
                        else
                            Append(GaOutputRecord.EmitInt32(value));
                    }
                    break;
                case GaEmitTyped typed:
                    var outputType = _eventTypes[typed.EventTypeIndex];
                    var outputRecord = outputType.IsContribution
                        ? GaOutputRecord.Contribute(typed.EventTypeIndex)
                        : GaOutputRecord.EmitTyped(typed.EventTypeIndex);
                    AppendPayload(outputRecord, outputType.Offsets, typed.Values);
                    break;
                case GaSetDomainOrder order:
                    int primary = unchecked((int)Read(order.Primary));
                    int secondary = unchecked((int)Read(order.Secondary));
                    if (Fault == null)
                    {
                        _domainPrimary = primary;
                        _domainSecondary = secondary;
                        _hasDomainKey = true;
                    }
                    break;
                case GaDestroy destroy:
                    int entityId = unchecked((int)Read(destroy.EntityId));
                    int generation = unchecked((int)Read(destroy.Generation));
                    if (Fault == null)
                        Append(GaOutputRecord.DestroyEntity(entityId, generation));
                    break;
                case GaCreate create:
                    if (create.TicketSlot >= 0)
                        _tickets[create.TicketSlot] = OutputCount;
                    Append(GaOutputRecord.CreateEntity());
                    break;
                case GaDestroyTicket destroy:
                    Append(GaOutputRecord.DestroyTicket(_tickets[destroy.TicketSlot]));
                    break;
                case GaRemove remove:
                    int removeId = unchecked((int)Read(remove.EntityId));
                    int removeGeneration = unchecked((int)Read(remove.Generation));
                    if (Fault == null)
                        Append(GaOutputRecord.RemoveEntity(remove.ComponentIndex, removeId, removeGeneration));
                    break;
                case GaRemoveTicket remove:
                    Append(GaOutputRecord.RemoveTicket(remove.ComponentIndex, _tickets[remove.TicketSlot]));
                    break;
                case GaAdd add:
                    int addId = unchecked((int)Read(add.EntityId));
                    int addGeneration = unchecked((int)Read(add.Generation));
                    if (Fault == null)
                        AppendPayload(GaOutputRecord.AddEntity(add.ComponentIndex, addId, addGeneration),
                            _additions[add.ComponentIndex].Offsets, add.Values);
                    break;
                case GaAddTicket add:
                    AppendPayload(GaOutputRecord.AddTicket(add.ComponentIndex, _tickets[add.TicketSlot]),
                        _additions[add.ComponentIndex].Offsets, add.Values);
                    break;
                case GaIf branch:
                    if (Read(branch.Condition) != 0)
                        Run(branch.Then);
                    else if (branch.Else != null)
                        Run(branch.Else);
                    break;
                case GaLoop loop:
                    for (int i = 0; Read(loop.Condition) != 0 && Fault == null; i++)
                    {
                        if (i == loop.MaximumIterations)
                        {
                            Fault = new GaKernelFault(GaFaultKind.IterationLimit, _evaluationOrdinal, loop.Condition.Source);
                            break;
                        }
                        Run(loop.Body);
                        Run(loop.Increment);
                        if (Fault != null)
                            break;
                    }
                    break;
            }
        }

        private void Append(GaOutputRecord record)
        {
            if (_stride == 0)
                _outputs[OutputCount] = record;
            else
                StageRecord(record);
            OutputCount++;
        }

        private void AppendPayload(GaOutputRecord record, int[] offsets, IReadOnlyList<GaAdditionValue> values)
        {
            var destination = StageRecord(record);
            foreach (var value in values)
            {
                ulong bits = Read(value.Value);
                if (Fault != null)
                    return;
                int offset = GaOutputRecord.Words + offsets[value.FieldIndex];
                destination[offset] = unchecked((uint)bits);
                if (value.Value.Type == GaScalar.Fixed)
                    destination[offset + 1] = unchecked((uint)(bits >> 32));
            }
            OutputCount++;
        }

        private Span<uint> StageRecord(GaOutputRecord record)
        {
            var destination = _records.Slice(OutputCount * _stride, _stride);
            record.Write(destination);
            if (_usesDomainOrder)
                GaOutputRecord.WriteDomainOrder(destination, _hasDomainKey, _domainPrimary, _domainSecondary);
            return destination;
        }

        private ulong StoreAndRead(GaSlot slot, GaValue value, GaValue body)
        {
            ulong bits = Read(value);
            if (Fault != null)
                return 0;
            Store(slot, bits);
            return Read(body);
        }

        private ulong Read(GaValue value)
        {
            if (Fault != null)
                return 0;
            switch (value)
            {
                case GaConstant constant:
                    return constant.Bits;
                case GaLoad load:
                    return Load(load.Slot);
                case GaBoundValue bound:
                    return StoreAndRead(bound.Slot, bound.Value, bound.Body);
                case GaStoredValue stored:
                    return StoreAndRead(stored.Slot, stored.Value, stored.Body);
                case GaBlockValue block:
                    Run(block.Body);
                    return Read(block.Result);
                case GaHasOwn present:
                    return _row[_presenceOffsets[present.ComponentIndex]];
                case GaResourceRead resource:
                    return ReadWords(_tickResources, _tickResourceOffsets[resource.FieldIndex], resource.Type);
                case GaBufferCount:
                    return (uint)_buffer!.Count;
                case GaBufferRead buffer:
                    return ReadBuffer(buffer);
                case GaTryRead optional:
                    uint found = _row[_presenceOffsets[optional.ComponentIndex]];
                    foreach (var copy in optional.Copies)
                        Store(copy.Local, found != 0 ? Load(copy.Field) : 0);
                    return found;
                case GaArrayRead indexed:
                    return TryIndex(indexed.Field, indexed.Index, indexed.Source, out var element) ? Load(element) : 0;
                case GaSelect select:
                    return Read(Read(select.Condition) != 0 ? select.True : select.False);
                case GaSnapshotRead lookup:
                    int entityId = unchecked((int)Read(lookup.EntityId));
                    int generation = unchecked((int)Read(lookup.Generation));
                    ulong fallback = Read(lookup.Fallback);
                    return Fault == null
                        ? _snapshot!.ReadFieldBits(entityId, generation, Math.Max(0, lookup.FieldIndex), lookup.Type, fallback)
                        : 0;
                case GaCast cast:
                    ulong source = Read(cast.Operand);
                    if (cast.Type == GaScalar.Fixed)
                        return unchecked((ulong)Fixed.FromInt32((int)source).Raw);
                    if (cast.Operand.Type == GaScalar.Fixed)
                        return unchecked((uint)(int)ReadFixed(source));
                    if (Fault == null && cast.Checked && cast.Type != cast.Operand.Type)
                    {
                        ulong ordinal = _evaluationOrdinal++;
                        if ((source & 0x80000000u) != 0)
                            Fault = new GaKernelFault(GaFaultKind.Overflow, ordinal, cast.Source);
                    }
                    return source;
                case GaUnary unary:
                    ulong bits = Read(unary.Operand);
                    if (unary.Type == GaScalar.Fixed)
                    {
                        if (Fault != null)
                            return 0;
                        ulong ordinal = _evaluationOrdinal++;
                        try { return unchecked((ulong)(-ReadFixed(bits)).Raw); }
                        catch (OverflowException)
                        {
                            Fault = new GaKernelFault(GaFaultKind.Overflow, ordinal, unary.Source);
                            return 0;
                        }
                    }
                    uint operand = unchecked((uint)bits);
                    if (Fault == null && unary.Checked)
                    {
                        ulong ordinal = _evaluationOrdinal++;
                        if (operand == 0x80000000u)
                            Fault = new GaKernelFault(GaFaultKind.Overflow, ordinal, unary.Source);
                    }
                    return unary.Operator switch
                    {
                        GaUnaryOperator.Not => operand ^ 1,
                        GaUnaryOperator.BitwiseNot => ~operand,
                        GaUnaryOperator.Negate => unchecked(0u - operand),
                        _ => throw new InvalidOperationException()
                    };
                case GaBinary binary:
                    ulong left = Read(binary.Left);
                    if (Fault != null || (binary.Operator == GaBinaryOperator.AndAlso && left == 0) ||
                        (binary.Operator == GaBinaryOperator.OrElse && left != 0))
                        return left;
                    ulong right = Read(binary.Right);
                    if (Fault != null)
                        return 0;
                    return binary.Left.Type == GaScalar.Fixed
                        ? ReadFixedBinary(binary, ReadFixed(left), ReadFixed(right))
                        : ReadBinary(binary, unchecked((uint)left), unchecked((uint)right));
                default:
                    throw new InvalidOperationException();
            }
        }

        private ulong ReadBuffer(GaBufferRead read)
        {
            int index = unchecked((int)Read(read.Index));
            if (Fault is not null)
                return 0;
            var buffer = _buffer!;
            ulong ordinal = _evaluationOrdinal++;
            if ((uint)index >= (uint)buffer.Count)
            {
                Fault = new GaKernelFault(GaFaultKind.IndexOutOfRange, ordinal, read.Source);
                return 0;
            }
            int offset = _bufferOffsets[read.FieldIndex];
            return read.Type switch
            {
                GaScalar.Fixed => buffer.ReadWide(index, offset),
                GaScalar.Bool => Bit(buffer.ReadWord(index, offset) != 0),
                _ => buffer.ReadWord(index, offset)
            };
        }

        private ulong ReadFixedBinary(GaBinary binary, Fixed left, Fixed right)
        {
            var op = binary.Operator;
            if (op is GaBinaryOperator.Add or GaBinaryOperator.Subtract or GaBinaryOperator.Multiply or GaBinaryOperator.Divide)
            {
                ulong ordinal = _evaluationOrdinal++;
                try
                {
                    var result = op switch
                    {
                        GaBinaryOperator.Add => left + right,
                        GaBinaryOperator.Subtract => left - right,
                        GaBinaryOperator.Multiply => left * right,
                        _ => left / right
                    };
                    return unchecked((ulong)result.Raw);
                }
                catch (OverflowException)
                {
                    Fault = new GaKernelFault(GaFaultKind.Overflow, ordinal, binary.Source);
                    return 0;
                }
                catch (DivideByZeroException)
                {
                    Fault = new GaKernelFault(GaFaultKind.DivideByZero, ordinal, binary.Source);
                    return 0;
                }
            }

            return Bit(op switch
            {
                GaBinaryOperator.Equal => left == right,
                GaBinaryOperator.NotEqual => left != right,
                GaBinaryOperator.Less => left < right,
                GaBinaryOperator.LessOrEqual => left <= right,
                GaBinaryOperator.Greater => left > right,
                GaBinaryOperator.GreaterOrEqual => left >= right,
                _ => throw new InvalidOperationException()
            });
        }

        private uint ReadBinary(GaBinary binary, uint left, uint right)
        {
            bool signed = binary.Left.Type == GaScalar.Int32;
            int x = unchecked((int)left);
            int y = unchecked((int)right);
            var op = binary.Operator;
            if (op is GaBinaryOperator.Divide or GaBinaryOperator.Remainder)
            {
                ulong ordinal = _evaluationOrdinal++;
                if (right == 0 || (signed && x == int.MinValue && y == -1))
                {
                    Fault = new GaKernelFault(right == 0 ? GaFaultKind.DivideByZero : GaFaultKind.Overflow,
                        ordinal, binary.Source);
                    return 0;
                }
                if (signed)
                    return unchecked((uint)(op == GaBinaryOperator.Divide ? x / y : x % y));
                return op == GaBinaryOperator.Divide ? left / right : left % right;
            }

            if (binary.Checked)
            {
                ulong ordinal = _evaluationOrdinal++;
                try
                {
                    return signed ? unchecked((uint)(op switch
                    {
                        GaBinaryOperator.Add => checked(x + y),
                        GaBinaryOperator.Subtract => checked(x - y),
                        GaBinaryOperator.Multiply => checked(x * y),
                        _ => throw new InvalidOperationException()
                    })) : op switch
                    {
                        GaBinaryOperator.Add => checked(left + right),
                        GaBinaryOperator.Subtract => checked(left - right),
                        GaBinaryOperator.Multiply => checked(left * right),
                        _ => throw new InvalidOperationException()
                    };
                }
                catch (OverflowException)
                {
                    Fault = new GaKernelFault(GaFaultKind.Overflow, ordinal, binary.Source);
                    return 0;
                }
            }

            int shift = y & 31;
            return op switch
            {
                GaBinaryOperator.Add => unchecked(left + right),
                GaBinaryOperator.Subtract => unchecked(left - right),
                GaBinaryOperator.Multiply => unchecked(left * right),
                GaBinaryOperator.BitwiseAnd => left & right,
                GaBinaryOperator.BitwiseOr => left | right,
                GaBinaryOperator.ExclusiveOr => left ^ right,
                GaBinaryOperator.LeftShift => left << shift,
                GaBinaryOperator.RightShift when signed => unchecked((uint)(x >> shift)),
                GaBinaryOperator.RightShift or GaBinaryOperator.UnsignedRightShift => left >> shift,
                GaBinaryOperator.Equal => Bit(left == right),
                GaBinaryOperator.NotEqual => Bit(left != right),
                GaBinaryOperator.Less => Bit(signed ? x < y : left < right),
                GaBinaryOperator.LessOrEqual => Bit(signed ? x <= y : left <= right),
                GaBinaryOperator.Greater => Bit(signed ? x > y : left > right),
                GaBinaryOperator.GreaterOrEqual => Bit(signed ? x >= y : left >= right),
                GaBinaryOperator.AndAlso or GaBinaryOperator.OrElse => right,
                _ => throw new InvalidOperationException()
            };
        }

        private static uint Bit(bool value) => value ? 1u : 0u;
        private static Fixed ReadFixed(ulong bits) => Fixed.FromRaw(unchecked((long)bits));
    }
}
