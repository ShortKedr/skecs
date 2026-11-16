using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA.Analysis;

public static partial class GaSemanticAnalysis
{
    private sealed partial class Verifier
    {
        public override void Visit(IOperation? operation)
        {
            if (operation == null)
                return;
            cancellationToken.ThrowIfCancellationRequested();
            var location = operation.Syntax.GetLocation();

            switch (operation)
            {
                case IInvalidOperation:
                case ILocalFunctionOperation:
                    // C# diagnoses malformed operations. Local functions are checked when called.
                    return;
                case IBlockOperation when operation.Syntax is UnsafeStatementSyntax || operation.Syntax.Parent is UnsafeStatementSyntax:
                    Report(Managed, location, "Remove unsafe code from the GA call graph.");
                    return;
                case IVariableDeclaratorOperation local when local.Symbol.RefKind != RefKind.None:
                    Report(Write, location, "Use a value local instead of a ref alias.");
                    return;
                case IVariableDeclaratorOperation local:
                    CheckType(local.Symbol.Type, location);
                    break;
                case ISimpleAssignmentOperation { IsRef: true }:
                    Report(Write, location, "Write the component value directly instead of reassigning its reference.");
                    return;
                case ISimpleAssignmentOperation assignment:
                    RequireWritable(assignment.Target, location);
                    break;
                case ICompoundAssignmentOperation compound:
                    RequireWritable(compound.Target, location);
                    CheckConversion(compound.InConversion, location);
                    if (compound.OperatorMethod != null)
                        VisitMethod(compound.OperatorMethod, location);
                    CheckConversion(compound.OutConversion, location);
                    break;
                case IIncrementOrDecrementOperation increment:
                    RequireWritable(increment.Target, location);
                    if (increment.OperatorMethod != null)
                        VisitMethod(increment.OperatorMethod, location);
                    break;
                case IFieldReferenceOperation field when field.Field.IsStatic && !field.Field.IsConst && !IsFixedConstant(field.Field):
                    Report(Managed, location, $"Pass '{field.Field.ToDisplayString()}' as a frozen value instead of reading shared state.");
                    return;
                case IPropertyReferenceOperation property:
                    AnalyzeProperty(property);
                    break;
                case IInvocationOperation invocation:
                    AnalyzeInvocation(invocation);
                    break;
                case IConversionOperation conversion when conversion.OperatorMethod != null:
                    VisitMethod(conversion.OperatorMethod, location);
                    break;
                case IBinaryOperation { OperatorMethod: not null, OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr }:
                    Report(Unknown, location, "Use bool short-circuit operators. The implicit truth test of an overloaded short-circuit operator has no GA registration.");
                    return;
                case IBinaryOperation binary when binary.OperatorMethod != null:
                    VisitMethod(binary.OperatorMethod, location);
                    break;
                case IUnaryOperation unary when unary.OperatorMethod != null:
                    VisitMethod(unary.OperatorMethod, location);
                    break;
                case ILoopOperation loop:
                    if (!HasFiniteBound(loop))
                        Report(Unknown, location, "Use a bounded loop with a stable integer limit and a unit step toward that limit.");
                    break;
                case IObjectCreationOperation creation when creation.Type?.IsValueType == true && creation.Initializer == null:
                    if (creation.Constructor is { IsImplicitlyDeclared: false } constructor)
                        VisitMethod(constructor, location);
                    break;
                case IBranchOperation branch when branch.BranchKind is BranchKind.Break or BranchKind.Continue:
                case IReturnOperation { Kind: OperationKind.Return }:
                case IBlockOperation or IExpressionStatementOperation or IVariableDeclarationGroupOperation or IVariableDeclarationOperation
                    or IVariableInitializerOperation or ILocalReferenceOperation or IParameterReferenceOperation
                    or IInstanceReferenceOperation or IFieldReferenceOperation or ILiteralOperation or IConversionOperation
                    or IBinaryOperation or IUnaryOperation or IConditionalOperation or IArgumentOperation
                    or IDefaultValueOperation or IParenthesizedOperation or IEmptyOperation or IDeclarationExpressionOperation
                    or IInlineArrayAccessOperation or ISwitchOperation or ISwitchCaseOperation or ISingleValueCaseClauseOperation
                    or IDefaultCaseClauseOperation:
                    break;
                default:
                    Report(Managed, location, $"Replace '{operation.Kind}' with a registered GA control-flow or value operation.");
                    return;
            }

            if (operation.Type is { } type && !(operation is IInstanceReferenceOperation && Equal(type, entry.ContainingType)))
                CheckType(type, location, operation is IParameterReferenceOperation);
            base.Visit(operation);
        }

        private void AnalyzeInvocation(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            var location = invocation.Syntax.GetLocation();
            if (Equal(method.ContainingType, context) && method.Name == "Emit" && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 0, ReturnsVoid: true } ||
                    method.Parameters.Length != 1 ||
                    method.Parameters[0].RefKind != RefKind.None ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
                    invocation.Instance is not IParameterReferenceOperation receiver ||
                    receiver.Parameter.RefKind != RefKind.In ||
                    !Equal(receiver.Parameter.ContainingSymbol, entry))
                    Report(Stream, location, "Call Emit(int) directly on the entry point's in SystemContext parameter.");
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name == "ReadSnapshotInt32" && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 0 } ||
                    method.ReturnType.SpecialType != SpecialType.System_Int32 ||
                    method.Parameters.Length != 3 ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                        parameter.Type.SpecialType != SpecialType.System_Int32) ||
                    invocation.Arguments.Length != 3 ||
                    invocation.Arguments[0].Parameter?.Ordinal != 0 ||
                    invocation.Arguments[1].Parameter?.Ordinal != 1 ||
                    invocation.Arguments[2].Parameter?.Ordinal != 2 ||
                    invocation.Instance is not IParameterReferenceOperation receiver ||
                    receiver.Parameter.RefKind != RefKind.In ||
                    !Equal(receiver.Parameter.ContainingSymbol, entry))
                    Report(Unknown, location, "Call ReadSnapshotInt32 directly on the entry point's in SystemContext parameter, with arguments in declaration order.");
                return;
            }
            if (AnalyzeIntrinsic(method, location))
            {
                CheckBorrowedArguments(invocation.Arguments, null, location);
                return;
            }
            if (IsForbiddenOwner(method.ContainingType) || IsForbiddenMethod(method))
            {
                Report(Managed, location, $"Replace '{method.ToDisplayString()}' with an explicit deterministic input or registered intrinsic.");
                return;
            }
            if (method.ContainingType.ToDisplayString() is "System.Math" or "System.MathF")
            {
                Report(Arithmetic, location, $"Arithmetic helper '{method.ToDisplayString()}' has no exact GA registration.");
                return;
            }

            CheckReceiver(method, invocation.Instance, location);
            CheckBorrowedArguments(invocation.Arguments, invocation.Instance, location);
            VisitMethod(method, location);
        }

        private bool IsRuntimeSystemContext() =>
            context is { IsRefLikeType: true } && context.ContainingAssembly.Identity.Name == "SKECS.GA" &&
            context.DeclaringSyntaxReferences.Length == 0 &&
            Equal(context.ContainingAssembly, compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute")?.ContainingAssembly);

        private void AnalyzeProperty(IPropertyReferenceOperation property)
        {
            var location = property.Syntax.GetLocation();
            if (IsForbiddenOwner(property.Property.ContainingType))
            {
                Report(Managed, location, $"Replace '{property.Property.ToDisplayString()}' with an explicit deterministic input.");
                return;
            }

            var write = property.Parent is IAssignmentOperation assignment && assignment.Target == property ||
                property.Parent is IIncrementOrDecrementOperation;
            if (write)
            {
                Report(Write, location, "Write a declared backing field instead of using a property setter.");
                return;
            }
            if (property.Property.GetMethod is not { } getter)
            {
                Report(Unknown, location, $"Provide a source getter or registered intrinsic for '{property.Property.Name}'.");
                return;
            }

            if (AnalyzeIntrinsic(getter, location))
                return;
            CheckReceiver(getter, property.Instance, location);
            CheckBorrowedArguments(property.Arguments, property.Instance, location);
            VisitMethod(getter, location);
        }

        private bool AnalyzeIntrinsic(IMethodSymbol method, Location location)
        {
            if (IsFixedIntrinsic(method) || Equal(method.ContainingType, entity) && method.Name is "get_Id" or "get_Version")
                return true;
            if (!Equal(method.ContainingType, context))
                return false;

            var descriptor = method.Name is "Emit" or "get_Commands" ? Stream : Unknown;
            Report(descriptor, location, $"Register and lower scoped context operation '{method.Name}' before using it in a GA kernel.");
            return true;
        }

        private bool IsFixedConstant(IFieldSymbol field) => Equal(field.ContainingType, fixedType) &&
            Equal(field.Type, fixedType) && field.IsReadOnly && field.Name is "Zero" or "One" or "MinValue" or "MaxValue";

        private bool IsFixedIntrinsic(IMethodSymbol method)
        {
            if (!Equal(method.ContainingType, fixedType) || method.Arity != 0 || !method.IsStatic ||
                method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
                return false;

            bool Args(params ITypeSymbol?[] types) => method.Parameters.Length == types.Length &&
                method.Parameters.Select(parameter => parameter.Type).Zip(types, Equal).All(matches => matches);
            var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
            var int64 = compilation.GetSpecialType(SpecialType.System_Int64);
            var boolean = compilation.GetSpecialType(SpecialType.System_Boolean);
            bool returnsFixed = Equal(method.ReturnType, fixedType);

            return method.Name switch
            {
                "FromRaw" or "FromInt64" => returnsFixed && Args(int64),
                "FromInt32" => returnsFixed && Args(int32),
                "FromRatio" => returnsFixed && Args(int64, int64),
                "op_Addition" or "op_Subtraction" or "op_Multiply" or "op_Division" => returnsFixed && Args(fixedType, fixedType),
                "op_UnaryNegation" => returnsFixed && Args(fixedType),
                "op_Equality" or "op_Inequality" or "op_LessThan" or "op_LessThanOrEqual" or
                    "op_GreaterThan" or "op_GreaterThanOrEqual" => Equal(method.ReturnType, boolean) && Args(fixedType, fixedType),
                "op_Explicit" => returnsFixed && (Args(int32) || Args(int64)) ||
                    (Equal(method.ReturnType, int32) || Equal(method.ReturnType, int64)) && Args(fixedType),
                _ => false
            };
        }

        private static bool IsForbiddenOwner(INamedTypeSymbol type) => type.ToDisplayString() is
            "System.DateTime" or "System.DateTimeOffset" or "System.TimeProvider" or "System.Diagnostics.Stopwatch"
            or "System.Environment" or "System.Random" or "System.Guid" or "SKECS.World"
            || type.ContainingNamespace.ToDisplayString() is "System.Threading" or "System.Reflection" or "System.IO";

        private static bool IsForbiddenMethod(IMethodSymbol method) => method.Name is "GetHashCode" or "GetType" or "MemberwiseClone";

        private void CheckReceiver(IMethodSymbol method, IOperation? receiver, Location location)
        {
            if (receiver != null && method.ContainingType.IsValueType && !method.IsReadOnly &&
                !Equal(method.ContainingType, entry.ContainingType) &&
                (WriteRoot(receiver) == null || HasReadonlyStorage(receiver)))
                Report(Write, location, $"Mark '{method.Name}' readonly or call it on a writable value owned by this invocation.");
        }

        private void CheckConversion(CommonConversion conversion, Location location)
        {
            if (conversion.MethodSymbol != null)
                VisitMethod(conversion.MethodSymbol, location);
        }

        private static bool HasReadonlyStorage(IOperation? operation) => operation switch
        {
            IFieldReferenceOperation field => field.Field.IsReadOnly || HasReadonlyStorage(field.Instance),
            IInlineArrayAccessOperation array => HasReadonlyStorage(array.Instance),
            IParenthesizedOperation parenthesized => HasReadonlyStorage(parenthesized.Operand),
            _ => false
        };

        private void CheckBorrowedArguments(ImmutableArray<IArgumentOperation> arguments, IOperation? receiver, Location location)
        {
            var borrows = new Dictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            if (receiver != null && StorageRoot(receiver) is { } receiverRoot)
                borrows.Add(receiverRoot, true);

            foreach (var argument in arguments)
            {
                var refKind = argument.Parameter?.RefKind;
                if (refKind is null or RefKind.None)
                    continue;
                bool writable = refKind is RefKind.Ref or RefKind.Out;
                if (writable)
                    RequireWritable(argument.Value, argument.Syntax.GetLocation());

                var root = StorageRoot(argument.Value);
                if (root == null)
                    continue;
                if (borrows.TryGetValue(root, out bool previouslyWritable) && (writable || previouslyWritable))
                    Report(Write, location, "Give each writable helper reference a distinct local or own-component root.");
                borrows[root] = writable || previouslyWritable;
            }
        }

        private void RequireWritable(IOperation operation, Location location)
        {
            if (WriteRoot(operation) == null)
                Report(Write, location, "Write only through a value local or a declared writable own-component root.");
        }

        private ISymbol? WriteRoot(IOperation operation)
        {
            var root = StorageRoot(operation);
            return root switch
            {
                IParameterSymbol parameter when parameter.RefKind is RefKind.None or RefKind.Ref or RefKind.Out &&
                    !Equal(parameter.Type, context) && !Equal(parameter.Type, entity) => parameter,
                ILocalSymbol { RefKind: RefKind.None } local => local,
                IMethodSymbol method when !Equal(method, entry) && !Equal(method.ContainingType, entry.ContainingType) &&
                    !method.IsReadOnly => method,
                _ => null
            };
        }

        private ISymbol? StorageRoot(IOperation? operation) => operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IDeclarationExpressionOperation declaration => StorageRoot(declaration.Expression),
            IFieldReferenceOperation field => StorageRoot(field.Instance),
            IInlineArrayAccessOperation array => StorageRoot(array.Instance),
            IParenthesizedOperation parenthesized => StorageRoot(parenthesized.Operand),
            IInstanceReferenceOperation when callChain.Count != 0 => callChain[callChain.Count - 1],
            _ => null
        };

        private bool HasFiniteBound(ILoopOperation loop)
        {
            IOperation? condition;
            IOperation? step;
            switch (loop)
            {
                case IForLoopOperation forLoop when forLoop.AtLoopBottom.Length == 1:
                    condition = forLoop.Condition;
                    step = forLoop.AtLoopBottom[0];
                    break;
                case IWhileLoopOperation { ConditionIsTop: true, Body: IBlockOperation block } whileLoop when
                    block.Operations.Length != 0 && !block.Descendants().OfType<IBranchOperation>().Any(branch =>
                        branch.BranchKind == BranchKind.Continue && Equal(branch.Target, whileLoop.ContinueLabel)):
                    condition = whileLoop.Condition;
                    step = block.Operations[block.Operations.Length - 1];
                    break;
                default:
                    return false;
            }

            if (step is IExpressionStatementOperation statement)
                step = statement.Operation;
            if (condition is not IBinaryOperation { OperatorMethod: null, LeftOperand: ILocalReferenceOperation counter } compare ||
                counter.Local.RefKind != RefKind.None || counter.Local.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int64 or SpecialType.System_UInt64))
                return false;

            int direction = step switch
            {
                IIncrementOrDecrementOperation { OperatorMethod: null, Target: ILocalReferenceOperation target }
                    when Equal(counter.Local, target.Local) => step.Kind == OperationKind.Increment ? 1 : -1,
                ICompoundAssignmentOperation { OperatorMethod: null, Target: ILocalReferenceOperation target } compound
                    when Equal(counter.Local, target.Local) && IsOne(compound.Value.ConstantValue) => compound.OperatorKind switch
                    {
                        BinaryOperatorKind.Add => 1,
                        BinaryOperatorKind.Subtract => -1,
                        _ => 0
                    },
                _ => 0
            };
            var comparisonMovesTowardLimit = (direction, compare.OperatorKind) switch
            {
                (1, BinaryOperatorKind.LessThan) or (-1, BinaryOperatorKind.GreaterThan) => true,
                (1, BinaryOperatorKind.LessThanOrEqual) or (-1, BinaryOperatorKind.GreaterThanOrEqual) =>
                    IsSafeInclusiveLimit(compare.RightOperand.ConstantValue, direction),
                _ => false
            };
            if (!comparisonMovesTowardLimit)
                return false;

            var bound = compare.RightOperand;
            var limit = StorageRoot(bound);
            if (!bound.ConstantValue.HasValue && (limit is not (ILocalSymbol { RefKind: RefKind.None } or
                IParameterSymbol { RefKind: RefKind.None or RefKind.In }) || Equal(counter.Local, limit)))
                return false;

            foreach (var bodyOperation in loop.Body.DescendantsAndSelf())
            {
                var written = bodyOperation switch
                {
                    IAssignmentOperation assignment => StorageRoot(assignment.Target),
                    IIncrementOrDecrementOperation increment => StorageRoot(increment.Target),
                    IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => StorageRoot(argument.Value),
                    IInvocationOperation invocation when !invocation.TargetMethod.IsReadOnly => StorageRoot(invocation.Instance),
                    IPropertyReferenceOperation property when property.Property.GetMethod?.IsReadOnly != true => StorageRoot(property.Instance),
                    _ => null
                };
                if (written != null && (Equal(written, limit) || Equal(written, counter.Local) && bodyOperation != step))
                    return false;
            }
            return true;
        }

        private static bool IsOne(Optional<object?> constant) => constant.HasValue && constant.Value switch
        {
            int value => value == 1,
            uint value => value == 1,
            long value => value == 1,
            ulong value => value == 1,
            _ => false
        };

        private static bool IsSafeInclusiveLimit(Optional<object?> constant, int direction) => constant.HasValue && constant.Value switch
        {
            int value => direction > 0 ? value < int.MaxValue : value > int.MinValue,
            uint value => direction > 0 ? value < uint.MaxValue : value > 0,
            long value => direction > 0 ? value < long.MaxValue : value > long.MinValue,
            ulong value => direction > 0 ? value < ulong.MaxValue : value > 0,
            _ => false
        };
    }
}
