namespace SKECS.GA;

internal sealed class GaEventCapacityException() : InvalidOperationException(
    $"An invocation may produce more than {GaEventPacket.MaximumCapacityPerSource.ToString(System.Globalization.CultureInfo.InvariantCulture)} events and commands.");

internal static class GaVerifier
{
    internal static (bool UsesFixed, bool UsesSnapshot, bool UsesCommands, int EventCapacity) Verify(GaProgram program)
    {
        bool usesFixed = program.Fields.Any(field => field.Type == GaScalar.Fixed) || program.Locals.Contains(GaScalar.Fixed);
        bool usesSnapshot = false;
        bool usesCommands = false;
        var declaredTickets = new HashSet<int>();
        var components = new Dictionary<string, GaFieldAccess>();
        foreach (var component in program.Components)
            Require(!string.IsNullOrWhiteSpace(component.Id) && Enum.IsDefined(component.Access)
                && components.TryAdd(component.Id, component.Access));
        var fields = new HashSet<(string Component, string Path)>();
        foreach (var field in program.Fields)
            Require(components.TryGetValue(field.Component, out var access) && access == field.Access
                && !string.IsNullOrWhiteSpace(field.Path) && fields.Add((field.Component, field.Path))
                && Enum.IsDefined(field.Type));
        Require(program.Locals.All(Enum.IsDefined) && program.TicketLocalCount >= 0);
        int eventCapacity = CheckStatement(program.Body, new HashSet<int>(), new HashSet<int>());
        Require(declaredTickets.Count == program.TicketLocalCount);
        if (eventCapacity > GaEventPacket.MaximumCapacityPerSource)
            throw new GaEventCapacityException();
        return (usesFixed, usesSnapshot, usesCommands, eventCapacity);

        void CheckSlot(GaSlot slot)
        {
            if (slot.IsField)
                Require((uint)slot.Index < program.Fields.Count && program.Fields[slot.Index].Type == slot.Type);
            else
                Require((uint)slot.Index < program.Locals.Count && program.Locals[slot.Index] == slot.Type);
        }

        int CheckStatement(GaStatement statement, HashSet<int> assigned, HashSet<int> assignedTickets)
        {
            switch (statement)
            {
                case GaBlock block:
                    int blockCapacity = 0;
                    foreach (var child in block.Statements)
                        blockCapacity = Bound((long)blockCapacity + CheckStatement(child, assigned, assignedTickets));
                    return blockCapacity;
                case GaStore store:
                    CheckSlot(store.Slot);
                    CheckValue(store.Value, assigned);
                    Require(store.Slot.Type == store.Value.Type);
                    if (store.Slot.IsField)
                        Require(program.Fields[store.Slot.Index].Access == GaFieldAccess.ReadWrite);
                    else
                        assigned.Add(store.Slot.Index);
                    return 0;
                case GaEmit emit:
                    CheckValue(emit.Value, assigned);
                    Require(emit.Value.Type == GaScalar.Int32);
                    return 1;
                case GaDestroy destroy:
                    usesCommands = true;
                    CheckValue(destroy.EntityId, assigned);
                    CheckValue(destroy.Generation, assigned);
                    Require(destroy.EntityId.Type == GaScalar.Int32 && destroy.Generation.Type == GaScalar.Int32);
                    return 1;
                case GaCreate create:
                    usesCommands = true;
                    if (create.TicketSlot != -1)
                    {
                        Require((uint)create.TicketSlot < (uint)program.TicketLocalCount &&
                            assignedTickets.Add(create.TicketSlot));
                        declaredTickets.Add(create.TicketSlot);
                    }
                    return 1;
                case GaDestroyTicket destroy:
                    usesCommands = true;
                    Require(assignedTickets.Contains(destroy.TicketSlot));
                    return 1;
                case GaIf branch:
                    CheckValue(branch.Condition, assigned);
                    Require(branch.Condition.Type == GaScalar.Bool);
                    var thenAssigned = new HashSet<int>(assigned);
                    var elseAssigned = new HashSet<int>(assigned);
                    var thenTickets = new HashSet<int>(assignedTickets);
                    var elseTickets = new HashSet<int>(assignedTickets);
                    int thenCapacity = CheckStatement(branch.Then, thenAssigned, thenTickets);
                    int elseCapacity = branch.Else == null ? 0 : CheckStatement(branch.Else, elseAssigned, elseTickets);
                    thenAssigned.IntersectWith(elseAssigned);
                    assigned.UnionWith(thenAssigned);
                    thenTickets.IntersectWith(elseTickets);
                    assignedTickets.UnionWith(thenTickets);
                    return Math.Max(thenCapacity, elseCapacity);
                case GaLoop loop:
                    CheckValue(loop.Condition, assigned);
                    Require(loop.Condition.Type == GaScalar.Bool && loop.MaximumIterations >= 0);
                    var loopAssigned = new HashSet<int>(assigned);
                    var loopTickets = new HashSet<int>(assignedTickets);
                    int loopCapacity = CheckStatement(loop.Body, loopAssigned, loopTickets) +
                        CheckStatement(loop.Increment, loopAssigned, loopTickets);
                    return Bound((long)loopCapacity * loop.MaximumIterations);
                default:
                    throw new InvalidDataException("Unknown GA IR statement.");
            }
        }

        static int Bound(long capacity) => (int)Math.Min(capacity, GaEventPacket.MaximumCapacityPerSource + 1L);

        void CheckValue(GaValue value, HashSet<int> assigned)
        {
            Require(Enum.IsDefined(value.Type));
            usesFixed |= value.Type == GaScalar.Fixed;
            switch (value)
            {
                case GaConstant constant:
                    Require(constant.Type switch
                    {
                        GaScalar.Bool => constant.Bits <= 1,
                        GaScalar.Fixed => true,
                        _ => constant.Bits <= uint.MaxValue
                    });
                    break;
                case GaLoad load:
                    CheckSlot(load.Slot);
                    Require(load.Slot.IsField || assigned.Contains(load.Slot.Index));
                    break;
                case GaCast cast:
                    CheckValue(cast.Operand, assigned);
                    Require((cast.Type, cast.Operand.Type) is
                        (GaScalar.Int32, GaScalar.UInt32 or GaScalar.Fixed) or
                        (GaScalar.UInt32 or GaScalar.Fixed, GaScalar.Int32));
                    break;
                case GaUnary unary:
                    CheckValue(unary.Operand, assigned);
                    Require(unary.Type == unary.Operand.Type && (unary.Operator switch
                    {
                        GaUnaryOperator.Negate => unary.Type is GaScalar.Int32 or GaScalar.Fixed,
                        GaUnaryOperator.BitwiseNot => unary.Type is GaScalar.Int32 or GaScalar.UInt32 && !unary.Checked,
                        GaUnaryOperator.Not => unary.Type == GaScalar.Bool && !unary.Checked,
                        _ => false
                    }));
                    break;
                case GaBinary binary:
                    CheckValue(binary.Left, assigned);
                    CheckValue(binary.Right, assigned);
                    CheckBinary(binary);
                    break;
                case GaSelect selected:
                    CheckValue(selected.Condition, assigned);
                    CheckValue(selected.True, assigned);
                    CheckValue(selected.False, assigned);
                    Require(selected.Condition.Type == GaScalar.Bool &&
                        selected.True.Type == selected.Type && selected.False.Type == selected.Type);
                    break;
                case GaSnapshotRead lookup:
                    usesSnapshot = true;
                    CheckValue(lookup.EntityId, assigned);
                    CheckValue(lookup.Generation, assigned);
                    CheckValue(lookup.Fallback, assigned);
                    Require(lookup.EntityId.Type == GaScalar.Int32 && lookup.Generation.Type == GaScalar.Int32 &&
                        lookup.Fallback.Type == GaScalar.Int32);
                    break;
                default:
                    throw new InvalidDataException("Unknown GA IR value.");
            }
        }
    }

    private static void CheckBinary(GaBinary op)
    {
        GaScalar operand = op.Left.Type;
        bool integer = operand is GaScalar.Int32 or GaScalar.UInt32;
        bool numeric = integer || operand == GaScalar.Fixed;
        bool homogeneous = op.Right.Type == operand;
        bool sameResult = op.Type == operand;
        bool boolResult = op.Type == GaScalar.Bool;
        bool valid = op.Operator switch
        {
            GaBinaryOperator.Add or GaBinaryOperator.Subtract or GaBinaryOperator.Multiply =>
                numeric && homogeneous && sameResult,
            GaBinaryOperator.Divide =>
                numeric && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.Remainder =>
                integer && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.BitwiseAnd or GaBinaryOperator.BitwiseOr or GaBinaryOperator.ExclusiveOr =>
                operand != GaScalar.Fixed && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.LeftShift or GaBinaryOperator.RightShift or GaBinaryOperator.UnsignedRightShift =>
                integer && sameResult && op.Right.Type == GaScalar.Int32 && !op.Checked,
            GaBinaryOperator.Equal or GaBinaryOperator.NotEqual => homogeneous && boolResult && !op.Checked,
            GaBinaryOperator.Less or GaBinaryOperator.LessOrEqual or GaBinaryOperator.Greater or GaBinaryOperator.GreaterOrEqual =>
                numeric && homogeneous && boolResult && !op.Checked,
            GaBinaryOperator.AndAlso or GaBinaryOperator.OrElse =>
                operand == GaScalar.Bool && homogeneous && boolResult && !op.Checked,
            _ => false
        };
        Require(valid);
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidDataException("GA IR violates its type, access or definite assignment contract.");
    }
}
