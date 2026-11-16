using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA;

internal sealed class GaLowerer(SourceReferenceResolver? resolver, IMethodSymbol entry)
{
    private readonly Dictionary<IParameterSymbol, GaComponent> _components = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(string Component, string Path), GaSlot> _fields = new();
    private readonly Dictionary<ILocalSymbol, GaSlot> _locals = new(SymbolEqualityComparer.Default);
    private readonly List<GaField> _schema = new();
    private readonly List<GaScalar> _localTypes = new();
    private bool _insideLoop;

    internal GaProgram Lower(string systemId, string sourceDigest, IOperation body)
    {
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in entry.Parameters)
        {
            if (parameter.RefKind == RefKind.None && QualifiedName(parameter.Type) == "global::SKECS.Entity")
                continue;
            if (!parameter.Type.IsUnmanagedType || parameter.Type.TypeKind != TypeKind.Struct ||
                parameter.RefKind is not (RefKind.Ref or RefKind.In) ||
                parameter.Type.SpecialType != SpecialType.None || HasExplicitLayout(parameter.Type))
                throw new GaLoweringException(parameter.Locations[0], "Expected a ref or in GA value component.");

            string id = QualifiedName(parameter.Type);
            if (!componentIds.Add(id))
                throw new GaLoweringException(parameter.Locations[0], "Two component parameters would alias the same row.");
            _components.Add(parameter, new GaComponent(id,
                parameter.RefKind == RefKind.Ref ? GaFieldAccess.ReadWrite : GaFieldAccess.Read));
        }
        var root = new GaBlock(new[] { LowerStatement(body) });
        var components = entry.Parameters.Where(_components.ContainsKey).Select(parameter => _components[parameter]).ToArray();
        return new GaProgram(systemId, sourceDigest, components, _schema.ToArray(), _localTypes.ToArray(), root);
    }

    private GaStatement LowerStatement(IOperation operation)
    {
        switch (operation)
        {
            case IBlockOperation block:
                return new GaBlock(block.Operations.Select(LowerStatement).ToArray());
            case IExpressionStatementOperation expression:
                return LowerStatement(expression.Operation);
            case IEmptyOperation:
                return new GaBlock(Array.Empty<GaStatement>());
            case IVariableDeclarationGroupOperation group:
                return new GaBlock(group.Declarations.Select(LowerStatement).ToArray());
            case IVariableDeclarationOperation declaration:
                return new GaBlock(declaration.Declarators.Select(LowerStatement).ToArray());
            case IVariableDeclaratorOperation declarator:
                if (declarator.Symbol.RefKind != RefKind.None)
                    throw Unsupported(declarator, "GA locals cannot alias storage.");
                var type = Scalar(declarator.Symbol.Type, declarator);
                var local = AddLocal(type);
                _locals.Add(declarator.Symbol, local);
                return declarator.Initializer == null
                    ? new GaBlock(Array.Empty<GaStatement>())
                    : new GaStore(local, LowerValue(declarator.Initializer.Value));
            case ISimpleAssignmentOperation assignment when !assignment.IsRef:
                return new GaStore(WritableSlot(assignment.Target), LowerValue(assignment.Value));
            case ICompoundAssignmentOperation compound when IsBuiltinOrFixed(compound.OperatorMethod) &&
                !compound.IsLifted && compound.InConversion.IsIdentity && compound.OutConversion.IsIdentity:
                var slot = WritableSlot(compound.Target);
                return new GaStore(slot, Binary(slot.Type, compound.OperatorKind,
                    new GaLoad(slot, Source(compound.Target)), LowerValue(compound.Value), compound.IsChecked, compound));
            case IIncrementOrDecrementOperation change when change.OperatorMethod == null && !change.IsLifted:
                var counter = WritableSlot(change.Target);
                if (counter.Type == GaScalar.Bool || change.Target.Type?.TypeKind == TypeKind.Enum)
                    throw Unsupported(change);
                return new GaStore(counter, new GaBinary(counter.Type,
                    change.Kind == OperationKind.Increment ? GaBinaryOperator.Add : GaBinaryOperator.Subtract,
                    new GaLoad(counter, Source(change.Target)), new GaConstant(counter.Type, 1, Source(change)),
                    change.IsChecked, Source(change)));
            case IConditionalOperation branch when branch.Type == null:
                return new GaIf(LowerValue(branch.Condition), LowerStatement(branch.WhenTrue),
                    branch.WhenFalse == null ? null : LowerStatement(branch.WhenFalse));
            case IForLoopOperation loop:
                return LowerFor(loop);
            case ISwitchOperation match:
                return LowerSwitch(match);
            default:
                throw Unsupported(operation);
        }
    }

    private GaStatement LowerFor(IForLoopOperation loop)
    {
        const int maximumTripCount = 65536;
        if (_insideLoop || loop.Before.Length != 1 || loop.AtLoopBottom.Length != 1
            || loop.Before[0] is not IVariableDeclarationGroupOperation { Declarations.Length: 1 } group
            || group.Declarations[0].Declarators.Length != 1
            || group.Declarations[0].Declarators[0] is not { Initializer.Value.ConstantValue.Value: int start } init
            || init.Symbol.Type.SpecialType != SpecialType.System_Int32
            || loop.Condition is not IBinaryOperation { LeftOperand: ILocalReferenceOperation conditionLocal,
                RightOperand.ConstantValue.Value: int end } comparison
            || !SymbolEqualityComparer.Default.Equals(init.Symbol, conditionLocal.Local)
            || loop.AtLoopBottom[0] is not IExpressionStatementOperation
                { Operation: IIncrementOrDecrementOperation
                    { Target: ILocalReferenceOperation incrementLocal } step }
            || !SymbolEqualityComparer.Default.Equals(init.Symbol, incrementLocal.Local))
            throw Unsupported(loop, "This profile requires a single int loop with constant bounds and a unit step.");

        long iterations = (step.Kind, comparison.OperatorKind) switch
        {
            (OperationKind.Increment, BinaryOperatorKind.LessThan) => Math.Max(0, (long)end - start),
            (OperationKind.Increment, BinaryOperatorKind.LessThanOrEqual) => Math.Max(0, (long)end - start + 1),
            (OperationKind.Decrement, BinaryOperatorKind.GreaterThan) => Math.Max(0, (long)start - end),
            (OperationKind.Decrement, BinaryOperatorKind.GreaterThanOrEqual) => Math.Max(0, (long)start - end + 1),
            _ => throw Unsupported(loop, "The loop condition must match the direction of its unit step.")
        };
        long finalCounter = step.Kind == OperationKind.Increment ? start + iterations : start - iterations;
        if (finalCounter < int.MinValue || finalCounter > int.MaxValue || iterations > maximumTripCount
            || loop.Body.DescendantsAndSelf().Any(op => op switch
            {
                IAssignmentOperation { Target: ILocalReferenceOperation target } =>
                    SymbolEqualityComparer.Default.Equals(target.Local, init.Symbol),
                IIncrementOrDecrementOperation { Target: ILocalReferenceOperation target } =>
                    SymbolEqualityComparer.Default.Equals(target.Local, init.Symbol),
                _ => false
            }))
            throw Unsupported(loop, "Cannot prove the loop's iteration range and induction-variable ownership.");

        _insideLoop = true;
        var before = LowerStatement(loop.Before[0]);
        var repeated = new GaLoop(LowerValue(comparison), LowerStatement(loop.Body),
            LowerStatement(loop.AtLoopBottom[0]), (int)iterations);
        _insideLoop = false;
        return new GaBlock(new[] { before, repeated });
    }

    private GaStatement LowerSwitch(ISwitchOperation match)
    {
        var value = LowerValue(match.Value);
        if (value.Type is not (GaScalar.Int32 or GaScalar.UInt32))
            throw Unsupported(match, "Use a 32-bit integer or enum switch value.");
        var test = AddLocal(value.Type);
        var arms = new List<(GaValue Condition, GaStatement Body)>();
        GaStatement? otherwise = null;
        foreach (var arm in match.Cases)
        {
            GaValue? condition = null;
            foreach (var clause in arm.Clauses)
            {
                if (clause is IDefaultCaseClauseOperation)
                    continue;
                if (clause is not ISingleValueCaseClauseOperation single || !single.Value.ConstantValue.HasValue)
                    throw Unsupported(clause, "Use constant case labels in the portable integer switch.");
                var source = Source(single);
                var equals = new GaBinary(GaScalar.Bool, GaBinaryOperator.Equal,
                    new GaLoad(test, source), LowerValue(single.Value), false, source);
                condition = condition == null ? equals :
                    new GaBinary(GaScalar.Bool, GaBinaryOperator.OrElse, condition, equals, false, source);
            }
            var body = LowerArm(arm.Body, match.ExitLabel);
            if (arm.Clauses.Any(clause => clause is IDefaultCaseClauseOperation))
                otherwise = body;
            if (condition != null)
                arms.Add((condition, body));
        }
        for (int i = arms.Count - 1; i >= 0; i--)
        {
            var (condition, body) = arms[i];
            otherwise = new GaIf(condition, body, otherwise);
        }
        return new GaBlock(otherwise == null
            ? new GaStatement[] { new GaStore(test, value) }
            : new GaStatement[] { new GaStore(test, value), otherwise });
    }

    private GaBlock LowerArm(IReadOnlyList<IOperation> operations, ILabelSymbol breakTarget)
    {
        var statements = new List<GaStatement>(operations.Count);
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            if (i == operations.Count - 1)
            {
                if (operation is IBranchOperation { BranchKind: BranchKind.Break } branch &&
                    SymbolEqualityComparer.Default.Equals(branch.Target, breakTarget))
                    break;
                if (operation is IBlockOperation block)
                {
                    statements.Add(LowerArm(block.Operations, breakTarget));
                    break;
                }
            }
            statements.Add(LowerStatement(operation));
        }
        return new GaBlock(statements);
    }

    private GaSlot AddLocal(GaScalar type)
    {
        var local = new GaSlot(false, _localTypes.Count, type);
        _localTypes.Add(type);
        return local;
    }

    private GaValue LowerValue(IOperation operation)
    {
        var type = Scalar(operation.Type, operation);
        var source = Source(operation);
        if (operation.ConstantValue is { HasValue: true } constant)
            return new GaConstant(type, constant.Value switch
            {
                bool enabled => enabled ? 1u : 0u,
                int value => unchecked((uint)value),
                uint value => value,
                _ => throw Unsupported(operation, "Only canonical 32-bit scalar constants are supported.")
            }, source);

        return operation switch
        {
            IFieldReferenceOperation field when field.Field.IsStatic && IsFixed(field.Field.ContainingType) =>
                new GaConstant(type, field.Field.Name switch
                {
                    "Zero" => 0,
                    "One" => 1ul << 32,
                    "MinValue" => 1ul << 63,
                    "MaxValue" => long.MaxValue,
                    _ => throw Unsupported(operation, "This Fixed member has no GA intrinsic lowering.")
                }, source),
            ILocalReferenceOperation or IFieldReferenceOperation => new GaLoad(Slot(operation), source),
            IParenthesizedOperation parentheses => LowerValue(parentheses.Operand),
            IConversionOperation conversion when conversion.OperatorMethod != null &&
                IsFixed(conversion.OperatorMethod.ContainingType) => ConvertFixed(type, LowerValue(conversion.Operand), conversion),
            IConversionOperation conversion when !conversion.Conversion.IsUserDefined &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsNumeric ||
                 conversion.Operand.Type?.TypeKind == TypeKind.Enum || conversion.Type?.TypeKind == TypeKind.Enum)
                => ConvertValue(type, conversion),
            IUnaryOperation { IsLifted: false } unary when IsBuiltinOrFixed(unary.OperatorMethod) => LowerUnary(type, unary),
            IBinaryOperation { IsLifted: false } binary when IsBuiltinOrFixed(binary.OperatorMethod) => Binary(type, binary.OperatorKind,
                LowerValue(binary.LeftOperand), LowerValue(binary.RightOperand), binary.IsChecked, binary),
            IInvocationOperation invocation when IsFixed(invocation.TargetMethod.ContainingType) => LowerFixedCall(invocation),
            IConditionalOperation conditional when conditional.WhenFalse != null && !conditional.IsRef =>
                new GaSelect(type, LowerValue(conditional.Condition), LowerValue(conditional.WhenTrue),
                    LowerValue(conditional.WhenFalse), source),
            IDefaultValueOperation => new GaConstant(type, 0, source),
            _ => throw Unsupported(operation)
        };
    }

    private GaValue ConvertValue(GaScalar type, IConversionOperation conversion)
    {
        var operand = LowerValue(conversion.Operand);
        return type == operand.Type ? operand : new GaCast(type, operand, conversion.IsChecked, Source(conversion));
    }

    private GaValue ConvertFixed(GaScalar type, GaValue operand, IOperation source)
    {
        if ((type, operand.Type) is not ((GaScalar.Fixed, GaScalar.Int32) or (GaScalar.Int32, GaScalar.Fixed)))
            throw Unsupported(source, "This Fixed profile supports exact conversions between int and Fixed.");
        return new GaCast(type, operand, false, Source(source));
    }

    private GaValue LowerFixedCall(IInvocationOperation invocation) => invocation switch
    {
        { TargetMethod.Name: "FromInt32", Arguments.Length: 1 } =>
            ConvertFixed(GaScalar.Fixed, LowerValue(invocation.Arguments[0].Value), invocation),
        { TargetMethod.Name: "FromRaw", Arguments.Length: 1 } when
            invocation.Arguments[0].Value.ConstantValue.Value is long raw =>
            new GaConstant(GaScalar.Fixed, unchecked((ulong)raw), Source(invocation)),
        _ => throw Unsupported(invocation, "This Fixed call requires a GA intrinsic for its exact signature.")
    };

    private GaValue LowerUnary(GaScalar type, IUnaryOperation operation)
    {
        var operand = LowerValue(operation.Operand);
        if (operation.OperatorKind == UnaryOperatorKind.Plus)
            return operand;
        var op = operation.OperatorKind switch
        {
            UnaryOperatorKind.Minus => GaUnaryOperator.Negate,
            UnaryOperatorKind.BitwiseNegation => GaUnaryOperator.BitwiseNot,
            UnaryOperatorKind.Not => GaUnaryOperator.Not,
            _ => throw Unsupported(operation)
        };
        return new GaUnary(type, op, operand,
            op == GaUnaryOperator.Negate && type != GaScalar.Fixed && operation.IsChecked, Source(operation));
    }

    private GaBinary Binary(GaScalar type, BinaryOperatorKind kind, GaValue left, GaValue right,
        bool isChecked, IOperation operation)
    {
        var op = kind switch
        {
            BinaryOperatorKind.Add => GaBinaryOperator.Add,
            BinaryOperatorKind.Subtract => GaBinaryOperator.Subtract,
            BinaryOperatorKind.Multiply => GaBinaryOperator.Multiply,
            BinaryOperatorKind.Divide => GaBinaryOperator.Divide,
            BinaryOperatorKind.Remainder => GaBinaryOperator.Remainder,
            BinaryOperatorKind.And => GaBinaryOperator.BitwiseAnd,
            BinaryOperatorKind.Or => GaBinaryOperator.BitwiseOr,
            BinaryOperatorKind.ExclusiveOr => GaBinaryOperator.ExclusiveOr,
            BinaryOperatorKind.LeftShift => GaBinaryOperator.LeftShift,
            BinaryOperatorKind.RightShift => GaBinaryOperator.RightShift,
            BinaryOperatorKind.UnsignedRightShift => GaBinaryOperator.UnsignedRightShift,
            BinaryOperatorKind.Equals => GaBinaryOperator.Equal,
            BinaryOperatorKind.NotEquals => GaBinaryOperator.NotEqual,
            BinaryOperatorKind.LessThan => GaBinaryOperator.Less,
            BinaryOperatorKind.LessThanOrEqual => GaBinaryOperator.LessOrEqual,
            BinaryOperatorKind.GreaterThan => GaBinaryOperator.Greater,
            BinaryOperatorKind.GreaterThanOrEqual => GaBinaryOperator.GreaterOrEqual,
            BinaryOperatorKind.ConditionalAnd => GaBinaryOperator.AndAlso,
            BinaryOperatorKind.ConditionalOr => GaBinaryOperator.OrElse,
            _ => throw Unsupported(operation)
        };
        bool canCheck = op is GaBinaryOperator.Add or GaBinaryOperator.Subtract or GaBinaryOperator.Multiply;
        return new GaBinary(type, op, left, right, canCheck && left.Type != GaScalar.Fixed && isChecked, Source(operation));
    }

    private GaSlot WritableSlot(IOperation operation)
    {
        var slot = Slot(operation);
        if (slot.IsField && _schema[slot.Index].Access != GaFieldAccess.ReadWrite)
            throw Unsupported(operation, "The target field was declared read-only.");
        return slot;
    }

    private GaSlot Slot(IOperation operation)
    {
        if (operation is ILocalReferenceOperation local && _locals.TryGetValue(local.Local, out var localSlot))
            return localSlot;
        if (operation is not IFieldReferenceOperation leaf)
            throw Unsupported(operation, "The target must be an owned component field or a scalar local.");

        var path = new Stack<string>();
        IOperation? receiver = leaf;
        while (receiver is IFieldReferenceOperation field && !field.Field.IsStatic
            && !HasExplicitLayout(field.Field.ContainingType))
        {
            path.Push(field.Field.MetadataName);
            receiver = field.Instance;
        }
        if (receiver is not IParameterReferenceOperation parameter || !_components.TryGetValue(parameter.Parameter, out var component))
            throw Unsupported(operation, "Fields must come from a non-overlaid component parameter.");

        string pathName = string.Join(".", path);
        var key = (component.Id, pathName);
        if (_fields.TryGetValue(key, out var fieldSlot))
            return fieldSlot;
        var type = Scalar(leaf.Type, leaf);
        fieldSlot = new GaSlot(true, _schema.Count, type);
        _fields.Add(key, fieldSlot);
        _schema.Add(new GaField(component.Id, pathName, type, component.Access)
        {
            ClrEnumType = leaf.Type is { TypeKind: TypeKind.Enum } enumType ? QualifiedName(enumType) : null
        });
        return fieldSlot;
    }

    private static bool HasExplicitLayout(ITypeSymbol type) => type.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
        attribute.ConstructorArguments[0].Value is 2);

    private static GaScalar Scalar(ITypeSymbol? type, IOperation operation)
    {
        if (IsFixed(type))
            return GaScalar.Fixed;
        if (type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
            type = underlying;
        return type?.SpecialType switch
        {
            SpecialType.System_Boolean => GaScalar.Bool,
            SpecialType.System_Int32 => GaScalar.Int32,
            SpecialType.System_UInt32 => GaScalar.UInt32,
            _ => throw Unsupported(operation, "This GA profile supports bool, int, uint, Fixed and enums with 32-bit integer backing.")
        };
    }

    private static bool IsBuiltinOrFixed(IMethodSymbol? method) => method == null || IsFixed(method.ContainingType);

    private static bool IsFixed(ITypeSymbol? type) =>
        type?.ContainingAssembly.Identity.Name == "SKECS" && QualifiedName(type) == "global::SKECS.Numerics.Fixed";

    private GaSourceSpan Source(IOperation operation)
    {
        var syntax = operation.Syntax;
        var path = syntax.SyntaxTree.FilePath;
        return new GaSourceSpan(resolver?.NormalizePath(path, null) ?? path, syntax.SpanStart, syntax.Span.Length);
    }

    private static string QualifiedName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static GaLoweringException Unsupported(IOperation operation, string? detail = null) =>
        new(operation.Syntax.GetLocation(), detail ?? $"{operation.Kind} has no lowering in the integer profile.");
}
