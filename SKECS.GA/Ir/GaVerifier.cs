namespace SKECS.GA;

internal static class GaVerifier
{
    internal static bool Verify(GaProgram program)
    {
        bool usesFixed = program.Fields.Any(field => field.Type == GaScalar.Fixed) || program.Locals.Contains(GaScalar.Fixed);
        var components = new Dictionary<string, GaFieldAccess>();
        foreach (var component in program.Components)
            Require(!string.IsNullOrWhiteSpace(component.Id) && Enum.IsDefined(component.Access)
                && components.TryAdd(component.Id, component.Access));
        var fields = new HashSet<(string Component, string Path)>();
        foreach (var field in program.Fields)
            Require(components.TryGetValue(field.Component, out var access) && access == field.Access
                && !string.IsNullOrWhiteSpace(field.Path) && fields.Add((field.Component, field.Path))
                && Enum.IsDefined(field.Type));
        Require(program.Locals.All(Enum.IsDefined));
        CheckStatement(program.Body, new HashSet<int>());
        return usesFixed;

        void CheckSlot(GaSlot slot)
        {
            if (slot.IsField)
                Require((uint)slot.Index < program.Fields.Count && program.Fields[slot.Index].Type == slot.Type);
            else
                Require((uint)slot.Index < program.Locals.Count && program.Locals[slot.Index] == slot.Type);
        }

        void CheckStatement(GaStatement statement, HashSet<int> assigned)
        {
            switch (statement)
            {
                case GaBlock block:
                    foreach (var child in block.Statements)
                        CheckStatement(child, assigned);
                    break;
                case GaStore store:
                    CheckSlot(store.Slot);
                    CheckValue(store.Value, assigned);
                    Require(store.Slot.Type == store.Value.Type);
                    if (store.Slot.IsField)
                        Require(program.Fields[store.Slot.Index].Access == GaFieldAccess.ReadWrite);
                    else
                        assigned.Add(store.Slot.Index);
                    break;
                case GaIf branch:
                    CheckValue(branch.Condition, assigned);
                    Require(branch.Condition.Type == GaScalar.Bool);
                    var thenAssigned = new HashSet<int>(assigned);
                    var elseAssigned = new HashSet<int>(assigned);
                    CheckStatement(branch.Then, thenAssigned);
                    if (branch.Else != null)
                        CheckStatement(branch.Else, elseAssigned);
                    thenAssigned.IntersectWith(elseAssigned);
                    assigned.UnionWith(thenAssigned);
                    break;
                case GaLoop loop:
                    CheckValue(loop.Condition, assigned);
                    Require(loop.Condition.Type == GaScalar.Bool && loop.MaximumIterations >= 0);
                    var loopAssigned = new HashSet<int>(assigned);
                    CheckStatement(loop.Body, loopAssigned);
                    CheckStatement(loop.Increment, loopAssigned);
                    break;
                default:
                    throw new InvalidDataException("Unknown GA IR statement.");
            }
        }

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
