using System.Runtime.InteropServices;
using SKECS.Numerics;

namespace SKECS.GA;

public enum GaFaultKind : uint { Overflow = 1, DivideByZero, IterationLimit }

public readonly record struct GaKernelFault(GaFaultKind Kind, ulong EvaluationOrdinal, GaSourceSpan Source);

public static class GaInterpreter
{
    public static GaKernelFault? Execute(GaProgram program, Span<uint> row)
    {
        ArgumentNullException.ThrowIfNull(program);
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
        GaSnapshotPacket? snapshot)
    {
        ArgumentNullException.ThrowIfNull(program);
        eventCount = 0;
        if (program.UsesSnapshot && snapshot is null)
            throw new InvalidOperationException("The GA program requires an Int32 snapshot binding.");
        if (program.UsesSnapshot && (snapshot!.Layout.Fields.Length != 1 || snapshot.Layout.Fields[0] != GaScalar.Int32))
            throw new ArgumentException("The GA snapshot binding must contain one Int32 field.", nameof(snapshot));
        if (row.Length != program.WordCount)
            throw new ArgumentException("The row length must match the packed GA schema.", nameof(row));
        if (events.Length < program.EventCapacity)
            throw new ArgumentException("The destination must hold the verified event capacity.", nameof(events));
        if (MemoryMarshal.Cast<uint, int>(row).Overlaps(events[..program.EventCapacity]))
            throw new ArgumentException("Event storage must not overlap the component row.", nameof(events));
        for (int i = 0; i < program.Fields.Count; i++)
            if (program.Fields[i].Type == GaScalar.Bool && row[program.Offsets[i]] > 1)
                throw new ArgumentException($"Field {i} is not a canonical boolean.", nameof(row));

        Span<uint> next = row.Length <= 128 ? stackalloc uint[row.Length] : new uint[row.Length];
        Span<ulong> locals = program.Locals.Count <= 64
            ? stackalloc ulong[program.Locals.Count]
            : new ulong[program.Locals.Count];
        Span<int> nextEvents = program.EventCapacity <= 128
            ? stackalloc int[program.EventCapacity]
            : new int[program.EventCapacity];
        row.CopyTo(next);
        var executor = new Executor(next, locals, nextEvents, program.Offsets, snapshot);
        executor.Run(program.Body);
        if (executor.Fault == null)
        {
            next.CopyTo(row);
            eventCount = executor.EventCount;
            nextEvents[..eventCount].CopyTo(events);
        }
        return executor.Fault;
    }

    private ref struct Executor
    {
        private readonly Span<uint> _row;
        private readonly Span<ulong> _locals;
        private readonly Span<int> _events;
        private readonly int[] _offsets;
        private readonly GaSnapshotPacket? _snapshot;
        private ulong _evaluationOrdinal;
        internal GaKernelFault? Fault { get; private set; }
        internal int EventCount { get; private set; }

        internal Executor(Span<uint> row, Span<ulong> locals, Span<int> events, int[] offsets, GaSnapshotPacket? snapshot)
        {
            _row = row;
            _locals = locals;
            _events = events;
            _offsets = offsets;
            _snapshot = snapshot;
        }

        private ulong Load(GaSlot slot)
        {
            if (!slot.IsField)
                return _locals[slot.Index];
            int offset = _offsets[slot.Index];
            return slot.Type == GaScalar.Fixed
                ? _row[offset] | (ulong)_row[offset + 1] << 32
                : _row[offset];
        }

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
                case GaEmit emit:
                    int value = unchecked((int)Read(emit.Value));
                    if (Fault == null)
                        _events[EventCount++] = value;
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
                case GaSelect select:
                    return Read(Read(select.Condition) != 0 ? select.True : select.False);
                case GaSnapshotRead lookup:
                    int entityId = unchecked((int)Read(lookup.EntityId));
                    int generation = unchecked((int)Read(lookup.Generation));
                    int fallback = unchecked((int)Read(lookup.Fallback));
                    return Fault == null ? unchecked((uint)_snapshot!.ReadInt32(entityId, generation, fallback)) : 0;
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
