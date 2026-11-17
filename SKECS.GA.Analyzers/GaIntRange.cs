using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA.Analysis;

internal static class GaIntRange
{
    internal static bool TryGet(IOperation expression, out (int Min, int Max) range)
    {
        expression = Strip(expression);
        range = default;
        if (expression.Type?.SpecialType != SpecialType.System_Int32)
            return false;
        if (expression.ConstantValue is { HasValue: true, Value: int constant })
        {
            range = (constant, constant);
            return true;
        }
        if (expression is IBinaryOperation { OperatorMethod: null, IsLifted: false } binary)
        {
            if (binary.OperatorKind == BinaryOperatorKind.And &&
                (NonnegativeConstant(binary.RightOperand) ?? NonnegativeConstant(binary.LeftOperand)) is { } mask)
            {
                range = (0, mask);
                return true;
            }
            if (binary.OperatorKind == BinaryOperatorKind.Remainder &&
                NonnegativeConstant(binary.RightOperand) is int divisor && divisor > 0)
            {
                int magnitude = divisor - 1;
                range = (-magnitude, magnitude);
                return true;
            }
        }
        if (expression is IConditionalOperation { IsRef: false, WhenFalse: { } otherwise } choose &&
            TryGet(choose.WhenTrue, out var thenRange) && TryGet(otherwise, out var elseRange))
        {
            range = (Math.Min(thenRange.Min, elseRange.Min), Math.Max(thenRange.Max, elseRange.Max));
            return true;
        }
        return false;
    }

    internal static bool TryGetFrozen(IOperation expression, ILoopOperation loop, Compilation compilation,
        CancellationToken cancellationToken, out (int Min, int Max) range)
    {
        expression = Strip(expression);
        range = default;
        if (expression.ConstantValue is { HasValue: true, Value: int constant })
        {
            range = (constant, constant);
            return true;
        }
        if (expression is not ILocalReferenceOperation { Local.RefKind: RefKind.None } reference ||
            reference.Local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var syntax = reference.Local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        if (syntax.SyntaxTree != loop.Syntax.SyntaxTree || syntax.Span.End > loop.Syntax.SpanStart ||
            compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax, cancellationToken) is not
                IVariableDeclaratorOperation { Initializer.Value: { } initializer } || !TryGet(initializer, out range))
            return false;

        var root = (IOperation)loop;
        int lastUse = loop.Syntax.Span.End;
        while (root.Parent is { } parent)
        {
            root = parent;
            // Later mutations in an enclosing loop may run before the next visit to this loop.
            if (root is ILoopOperation)
                lastUse = root.Syntax.Span.End;
        }
        foreach (var operation in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Syntax.SpanStart >= syntax.Span.End && operation.Syntax.SpanStart < lastUse &&
                WritesLocal(operation, reference.Local))
                return false;
        }
        return true;
    }

    private static int? NonnegativeConstant(IOperation operand) =>
        operand.ConstantValue is { HasValue: true, Value: int value } && value >= 0 ? value : (int?)null;

    private static IOperation Strip(IOperation expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IParenthesizedOperation parentheses:
                    expression = parentheses.Operand;
                    break;
                case IConversionOperation { Conversion.IsIdentity: true, OperatorMethod: null } identity:
                    expression = identity.Operand;
                    break;
                default:
                    return expression;
            }
        }
    }

    internal static int UnitStepDirection(IOperation? step, ILocalSymbol counter) => step switch
    {
        IIncrementOrDecrementOperation { OperatorMethod: null, Target: ILocalReferenceOperation target }
            when SymbolEqualityComparer.Default.Equals(counter, target.Local) => step.Kind == OperationKind.Increment ? 1 : -1,
        ICompoundAssignmentOperation { OperatorMethod: null, Target: ILocalReferenceOperation target } compound
            when SymbolEqualityComparer.Default.Equals(counter, target.Local) &&
                compound.Value.ConstantValue is { HasValue: true, Value: 1 or 1u or 1L or 1UL } => compound.OperatorKind switch
            {
                BinaryOperatorKind.Add => 1,
                BinaryOperatorKind.Subtract => -1,
                _ => 0
            },
        _ => 0
    };

    internal static bool WritesLocal(IOperation operation, ILocalSymbol local) => operation switch
    {
        ISimpleAssignmentOperation { IsRef: true } assignment =>
            ReferencesLocal(assignment.Target, local) || ReferencesLocal(assignment.Value, local),
        IAssignmentOperation assignment => ReferencesLocal(assignment.Target, local),
        IIncrementOrDecrementOperation step => ReferencesLocal(step.Target, local),
        IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => ReferencesLocal(argument.Value, local),
        IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None, Initializer.Value: { } value } => ReferencesLocal(value, local),
        _ => false
    };

    private static bool ReferencesLocal(IOperation? target, ILocalSymbol local) =>
        target != null && (Strip(target) switch
        {
            ILocalReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Local, local),
            ITupleOperation tuple => tuple.Elements.Any(element => ReferencesLocal(element, local)),
            IDeclarationExpressionOperation declaration => ReferencesLocal(declaration.Expression, local),
            _ => false
        });
}
