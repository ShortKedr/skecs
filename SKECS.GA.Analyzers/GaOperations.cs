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
        private enum OwnMode { Read, ReadWrite, OptionalRead }

        private readonly Dictionary<ITypeSymbol, OwnMode> ownAccesses = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, ITypeSymbol> ownAliases = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ILocalSymbol> resourceAliases = new(SymbolEqualityComparer.Default);
        private ITypeSymbol? resourceType;
        private ITypeSymbol? bufferType;

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
                    if (local.Symbol.RefKind == RefKind.RefReadOnly &&
                        StripParentheses(local.Initializer?.Value) is IInvocationOperation resource &&
                        IsTickResourceCall(resource) && Equal(resourceType, local.Symbol.Type))
                    {
                        resourceAliases.Add(local.Symbol);
                        break;
                    }
                    if (local.Symbol.RefKind != RefKind.Ref ||
                        StripParentheses(local.Initializer?.Value) is not IInvocationOperation own ||
                        !TryOwnType(own, out var ownType) || !Equal(ownType, local.Symbol.Type))
                    {
                        Report(Write, location, "Use a direct ref local for Own<T>() or ref readonly local for Resource<T>().");
                        return;
                    }
                    ownAliases.Add(local.Symbol, ownType);
                    break;
                case IVariableDeclaratorOperation local when IsEntityTicket(local.Symbol.Type):
                    if (local.Initializer?.Value is IInvocationOperation createCall && IsTicketCreation(createCall))
                        ticketLocals.Add(local.Symbol);
                    else
                        Report(Stream, location, "Initialize a GA ticket local directly with the entry context's Create().");
                    break;
                case IVariableDeclaratorOperation local:
                    CheckType(local.Symbol.Type, location);
                    break;
                case ISimpleAssignmentOperation { IsRef: true }:
                    Report(Write, location, "Write the component value directly instead of reassigning its reference.");
                    return;
                case ISimpleAssignmentOperation fieldInitializer when IsPackedOutputInitializer(fieldInitializer.Parent):
                    if (fieldInitializer.Target is not IFieldReferenceOperation { Field.IsStatic: false })
                        Report(Stream, location, "Initialize only instance fields in a GA output payload.");
                    break;
                case ISimpleAssignmentOperation initializerAssignment when
                    initializerAssignment.Parent is IObjectOrCollectionInitializerOperation valueInitializer &&
                    IsPlainValueCreation(valueInitializer.Parent):
                    if (initializerAssignment.Target is not IFieldReferenceOperation { Field.IsStatic: false })
                        Report(Unknown, location, "Initialize only explicit instance fields in a portable value.");
                    break;
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
                case IFieldReferenceOperation field when IsInlineArrayBackingField(field.Field):
                    Report(Unknown, location, "Access an inline array backing field through index 0.");
                    return;
                case ILocalReferenceOperation local when ownAliases.ContainsKey(local.Local) && !IsComponentReceiver(local):
                    Report(Write, location, "Keep a ref to an own component local and access its fields directly.");
                    return;
                case ILocalReferenceOperation local when resourceAliases.Contains(local.Local):
                    CheckResourceUse(local, location);
                    break;
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
                case IInlineArrayAccessOperation access:
                    AnalyzeInlineArrayAccess(access, location);
                    break;
                case IObjectCreationOperation payload when IsPackedOutputValue(payload):
                    if (payload.Arguments.Length != 0 || payload.Constructor is { IsImplicitlyDeclared: false })
                        Report(Stream, location, "Use a plain field initializer with no user-defined constructor in a GA output payload.");
                    break;
                case IObjectOrCollectionInitializerOperation initializer when
                    IsPackedOutputInitializer(initializer) || IsPlainValueCreation(initializer.Parent):
                    break;
                case IObjectCreationOperation creation when IsPlainValueCreation(creation):
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
                    or IDiscardOperation
                    or ISwitchOperation or ISwitchCaseOperation or ISingleValueCaseClauseOperation
                    or IDefaultCaseClauseOperation:
                    break;
                default:
                    Report(Managed, location, $"Replace '{operation.Kind}' with a registered GA control-flow or value operation.");
                    return;
            }

            if (operation.Type is { } type)
            {
                if (IsEntityTicket(type))
                {
                    if (!IsTicketUse(operation))
                        Report(Stream, location, "Keep GA tickets in their initial local and pass them directly to Destroy, Remove or Add.");
                }
                else if (!(operation is IInstanceReferenceOperation && Equal(type, entry.ContainingType)))
                    CheckType(type, location, operation is IParameterReferenceOperation);
            }
            base.Visit(operation);
        }

        private void AnalyzeInlineArrayAccess(IInlineArrayAccessOperation access, Location location)
        {
            if (access.Argument.Type?.SpecialType != SpecialType.System_Int32)
                Report(Unknown, location, "Index an inline array element with Int32.");
            var root = StorageRoot(access.Instance);
            bool query = root is IParameterSymbol parameter && Equal(parameter.ContainingSymbol, entry) &&
                parameter.RefKind is (RefKind.In or RefKind.Ref);
            bool own = root is ITypeSymbol component && ownAccesses.ContainsKey(component);
            if (!query && !own)
                Report(Unknown, location, "Use an inline array stored in a query component or a declared own component.");
        }

        private bool IsInlineArrayBackingField(IFieldSymbol field) => !field.IsStatic &&
            field.ContainingType.GetAttributes().Any(attribute =>
                attribute.AttributeClass is { } type &&
                type.MetadataName == "InlineArrayAttribute" &&
                Equal(type, compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InlineArrayAttribute")));

        private void AnalyzeInvocation(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            var location = invocation.Syntax.GetLocation();
            if (Equal(method.ContainingType, context) && method.Name == "Resource" && IsRuntimeSystemContext())
            {
                if (!IsTickResourceCall(invocation))
                    Report(Unknown, location, "Call Resource<T>() directly on the entry context with the declared runner tick input type.");
                else if (ParentOutsideParentheses(invocation) is not IVariableInitializerOperation
                    { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.RefReadOnly } })
                    CheckResourceUse(invocation, location);
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name is ("ReadBuffer" or "BufferLength") &&
                IsRuntimeSystemContext())
            {
                AnalyzeBufferAccess(invocation, location);
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name is ("Own" or "Has" or "TryRead") && IsRuntimeSystemContext())
            {
                AnalyzeOwnAccess(invocation, location);
                return;
            }
            if (IsAdditionCommand(method))
            {
                var component = method.TypeArguments[0];
                if (!IsEntryContext(invocation.Instance) || !IsRemovalComponent(component) ||
                    !CanPackOutput(component))
                {
                    Report(Stream, location, "Call Add<T> directly on the entry context with an accessible, portable component struct.");
                }
                else if (invocation.Arguments.Length != method.Parameters.Length ||
                    invocation.Arguments.Where((argument, index) => argument.Parameter?.Ordinal != index).Any())
                {
                    Report(Stream, location, "Call Add<T> with arguments in declaration order.");
                }
                else if (IsTicketCommand(method) && !HasCreatedTicket(invocation))
                {
                    Report(Stream, location, "Pass a ticket local initialized by this invocation's Create().");
                }
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name == "Remove" && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 1, ReturnsVoid: true } || !IsEntryContext(invocation.Instance) ||
                    !IsRemovalComponent(method.TypeArguments[0]))
                {
                    Report(Stream, location, "Call Remove<T> directly on the entry context with an accessible, closed unmanaged component struct.");
                }
                else if (IsTicketCommand(method))
                {
                    if (!HasCreatedTicket(invocation))
                        Report(Stream, location, "Pass a ticket local initialized by this invocation's Create().");
                }
                else if (method.Parameters.Length != 2 ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                        parameter.Type.SpecialType != SpecialType.System_Int32) ||
                    invocation.Arguments.Length != 2 ||
                    invocation.Arguments[0].Parameter?.Ordinal != 0 ||
                    invocation.Arguments[1].Parameter?.Ordinal != 1)
                {
                    Report(Stream, location, "Call Remove<T>(int, int) with arguments in declaration order.");
                }
                return;
            }
            if (IsTicketCommand(method))
            {
                if (!IsEntryContext(invocation.Instance) || !HasCreatedTicket(invocation))
                    Report(Stream, location, "Call Destroy(ticket) directly on the entry context with a local initialized by Create().");
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name == "Create" && IsEntityTicket(method.ReturnType))
                return;
            if (IsTypedOutput(method))
            {
                AnalyzeTypedOutput(invocation, location);
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name == "Emit" && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 0, ReturnsVoid: true } ||
                    method.Parameters.Length != 1 ||
                    method.Parameters[0].RefKind != RefKind.None ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
                    !IsEntryContext(invocation.Instance))
                    Report(Stream, location, "Call Emit(int) directly on the entry point's in SystemContext parameter.");
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name is ("Destroy" or "SetDomainOrder") && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 0, ReturnsVoid: true } ||
                    method.Parameters.Length != 2 ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                        parameter.Type.SpecialType != SpecialType.System_Int32) ||
                    invocation.Arguments.Length != 2 ||
                    invocation.Arguments[0].Parameter?.Ordinal != 0 ||
                    invocation.Arguments[1].Parameter?.Ordinal != 1 ||
                    !IsEntryContext(invocation.Instance))
                    Report(Stream, location, $"Call {method.Name}(int, int) directly on the entry point's in SystemContext parameter, with arguments in declaration order.");
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
                    !IsEntryContext(invocation.Instance))
                    Report(Unknown, location, "Call ReadSnapshotInt32 directly on the entry point's in SystemContext parameter, with arguments in declaration order.");
                else if (!snapshotFields.IsDefault && (snapshotFields.Length != 1 || !IsSnapshotScalar(snapshotFields[0], "Int32")))
                    Report(Unknown, location, "ReadSnapshotInt32 requires the single Int32 snapshot layout.");
                return;
            }
            if (Equal(method.ContainingType, context) && method.Name == "ReadSnapshot" && IsRuntimeSystemContext())
            {
                if (method is not { IsStatic: false, Arity: 1 } || !IsEntryContext(invocation.Instance) ||
                    invocation.Arguments.Length != 4 ||
                    invocation.Arguments.Where((argument, index) => argument.Parameter?.Ordinal != index).Any() ||
                    invocation.Arguments[2].Value.ConstantValue is not { HasValue: true, Value: int field })
                {
                    Report(Unknown, location, "Call ReadSnapshot<T> directly on the entry point's in SystemContext parameter, with arguments in declaration order and a constant field index.");
                    return;
                }
                var type = method.TypeArguments[0];
                string? scalar = type.SpecialType switch
                {
                    SpecialType.System_Boolean => "Bool",
                    SpecialType.System_Int32 => "Int32",
                    SpecialType.System_UInt32 => "UInt32",
                    _ when Equal(type, fixedType) => "Fixed",
                    _ => null
                };
                if (scalar is null || snapshotFields.IsDefault || (uint)field >= (uint)snapshotFields.Length ||
                    !IsSnapshotScalar(snapshotFields[field], scalar))
                    Report(Unknown, location, "ReadSnapshot<T> requires a supported scalar type matching the declared snapshot field.");
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

        private ITypeSymbol? ReadDeclaredInputType(string attributeName, string description)
        {
            ITypeSymbol? declared = null;
            foreach (var attribute in entry.GetAttributes())
            {
                if (attribute.AttributeClass is not { } type || type.Name != attributeName + "Attribute" ||
                    type.ContainingNamespace.ToDisplayString() != "SKECS.GA")
                    continue;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? Source(entry);
                if (declared != null || !IsRuntimeSystemContext() || !Equal(type.ContainingAssembly, context?.ContainingAssembly) ||
                    type.DeclaringSyntaxReferences.Length != 0 || !entry.Parameters.Any(parameter => Equal(parameter.Type, context)) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol input } ||
                    !(IsRemovalComponent(input) || IsScalarResource(input) && CanNameComponentType(input)) ||
                    !IsSchema(input, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
                {
                    Report(Unknown, location, $"Declare one portable {description} with the runtime {attributeName} attribute.");
                    continue;
                }
                declared = input;
            }
            return declared;
        }

        private void AnalyzeBufferAccess(IInvocationOperation invocation, Location location)
        {
            var method = invocation.TargetMethod;
            bool readsElement = method.Name == "ReadBuffer";
            bool valid = method is { IsStatic: false, Arity: 1 } &&
                IsEntryContext(invocation.Instance) && bufferType != null &&
                Equal(method.TypeArguments[0], bufferType);
            valid = valid && (readsElement
                ? method.ReturnsByRefReadonly && Equal(method.ReturnType, bufferType) &&
                    method.Parameters.Length == 1 && invocation.Arguments.Length == 1 &&
                    method.Parameters[0].RefKind == RefKind.None &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_Int32
                : !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
                    method.Parameters.Length == 0 && invocation.Arguments.Length == 0 &&
                    method.ReturnType.SpecialType == SpecialType.System_Int32);
            if (!valid)
            {
                Report(Unknown, location,
                    "Call ReadBuffer<T>(int) or BufferLength<T>() directly on the entry context with the declared buffer element type.");
                return;
            }
            if (!readsElement)
                return;

            IOperation value = invocation;
            while (ParentOutsideParentheses(value) is IFieldReferenceOperation field)
                value = field;
            if (value.Type is null || !IsScalarResource(value.Type))
                Report(Unknown, location, "Read a portable scalar field from the declared buffer.");
            if (ParentOutsideParentheses(value) is IArgumentOperation { Parameter.RefKind: not RefKind.None } or
                IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } })
                Report(Write, location, "Copy the buffer field into a scalar local before using it by reference.");
        }

        private bool IsTickResourceCall(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            return method is { Name: "Resource", IsStatic: false, Arity: 1, ReturnsByRefReadonly: true } &&
                Equal(method.ContainingType, context) && IsRuntimeSystemContext() && IsEntryContext(invocation.Instance) &&
                invocation.Arguments.Length == 0 && resourceType != null &&
                Equal(method.TypeArguments[0], resourceType) && Equal(method.ReturnType, resourceType);
        }

        private bool IsScalarResource(ITypeSymbol type) => Equal(type, fixedType) ||
            type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32 or SpecialType.System_UInt32 ||
            type is INamedTypeSymbol
            {
                IsFileLocal: false,
                EnumUnderlyingType.SpecialType: SpecialType.System_Int32 or SpecialType.System_UInt32
            };

        private void CheckResourceUse(IOperation operation, Location location)
        {
            if (ParentOutsideParentheses(operation) is not IFieldReferenceOperation &&
                (operation.Type == null || !IsScalarResource(operation.Type)))
                Report(Unknown, location, "Read scalar fields of the declared tick resource.");
        }

        private bool IsResourcePath(IOperation? operation)
        {
            var root = StripParentheses(operation);
            while (root is IFieldReferenceOperation field)
                root = StripParentheses(field.Instance);
            return root is IInvocationOperation invocation && IsTickResourceCall(invocation) ||
                root is ILocalReferenceOperation local && resourceAliases.Contains(local.Local);
        }

        private void ValidateOwnAccesses(HashSet<ITypeSymbol> components)
        {
            foreach (var attribute in entry.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "GaAccessAttribute" } type ||
                    type.ContainingNamespace.ToDisplayString() != "SKECS.GA")
                    continue;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? Source(entry);
                if (!IsRuntimeSystemContext() || !Equal(type.ContainingAssembly, context?.ContainingAssembly) ||
                    type.DeclaringSyntaxReferences.Length != 0 || !entry.Parameters.Any(parameter => Equal(parameter.Type, context)) ||
                    attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0] is not { Kind: TypedConstantKind.Type, Value: ITypeSymbol component } ||
                    !IsRemovalComponent(component) || !IsSchema(component, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
                    ReadOwnMode(attribute.ConstructorArguments[1]) is not { } mode)
                {
                    Report(Unknown, location, "Declare a portable own component and access mode with the runtime GaAccess attribute.");
                    continue;
                }
                if (!components.Add(component))
                    Report(Unknown, location, "Declare each component once, either as a query parameter or with GaAccess.");
                else
                    ownAccesses.Add(component, mode);
            }
        }

        private static OwnMode? ReadOwnMode(TypedConstant value)
        {
            if (IsSnapshotScalar(value, "Read")) return OwnMode.Read;
            if (IsSnapshotScalar(value, "ReadWrite")) return OwnMode.ReadWrite;
            if (IsSnapshotScalar(value, "OptionalRead")) return OwnMode.OptionalRead;
            return null;
        }

        private void AnalyzeOwnAccess(IInvocationOperation invocation, Location location)
        {
            var method = invocation.TargetMethod;
            if (method.Name == "Own")
            {
                if (!TryOwnType(invocation, out _))
                {
                    Report(Unknown, location, "Call Own<T> directly on the entry context for a declared required own component.");
                    return;
                }
                if (!IsComponentReceiver(invocation) &&
                    ParentOutsideParentheses(invocation) is not IVariableInitializerOperation
                    { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } })
                    Report(Write, location, "Access fields of Own<T> directly or through a ref local.");
                return;
            }
            if (!IsEntryContext(invocation.Instance) || method is not { IsStatic: false, Arity: 1 } ||
                method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                !ownAccesses.TryGetValue(method.TypeArguments[0], out var access) || access != OwnMode.OptionalRead)
            {
                Report(Unknown, location, "Call Has<T> or TryRead<T> directly on the entry context for a declared OptionalRead component.");
                return;
            }
            if (method.Name == "Has")
            {
                if (invocation.Arguments.Length != 0)
                    Report(Unknown, location, "Call Has<T> without arguments.");
                return;
            }
            var argument = invocation.Arguments.Length == 1 ? invocation.Arguments[0] : null;
            var target = StripParentheses(argument?.Value);
            if (target is IDeclarationExpressionOperation declaration)
                target = declaration.Expression;
            if (argument?.Parameter?.RefKind != RefKind.Out ||
                target is not (ILocalReferenceOperation { Local.RefKind: RefKind.None } or IDiscardOperation))
                Report(Unknown, location, "Pass a value local or discard as the out argument to TryRead<T>.");
        }

        private bool TryOwnType(IInvocationOperation invocation, out ITypeSymbol type)
        {
            var method = invocation.TargetMethod;
            if (method is { Name: "Own", IsStatic: false, Arity: 1, ReturnsByRef: true } &&
                Equal(method.ContainingType, context) && IsRuntimeSystemContext() && IsEntryContext(invocation.Instance) &&
                invocation.Arguments.Length == 0 && ownAccesses.TryGetValue(method.TypeArguments[0], out var mode) &&
                mode != OwnMode.OptionalRead)
            {
                type = method.TypeArguments[0];
                return true;
            }
            type = null!;
            return false;
        }

        private static bool IsComponentReceiver(IOperation operation) =>
            ParentOutsideParentheses(operation) is IFieldReferenceOperation or IInlineArrayAccessOperation;

        private static IOperation? ParentOutsideParentheses(IOperation operation)
        {
            while (operation.Parent is IParenthesizedOperation parenthesized)
                operation = parenthesized;
            return operation.Parent;
        }

        private static IOperation? StripParentheses(IOperation? operation)
        {
            while (operation is IParenthesizedOperation parenthesized)
                operation = parenthesized.Operand;
            return operation;
        }

        private bool IsEntryContext(IOperation? receiver) =>
            receiver is IParameterReferenceOperation reference &&
            reference.Parameter.RefKind == RefKind.In &&
            Equal(reference.Parameter.ContainingSymbol, entry);

        private bool IsEntityTicket(ITypeSymbol type) =>
            type.IsRefLikeType && Equal(type, ticket) && Equal(type.ContainingAssembly, context?.ContainingAssembly) &&
            IsRuntimeSystemContext();

        private bool IsTicketCreation(IInvocationOperation invocation) =>
            invocation.TargetMethod is { Name: "Create", IsStatic: false, Arity: 0, Parameters.Length: 0 } method &&
            Equal(method.ContainingType, context) && IsEntityTicket(method.ReturnType) && IsEntryContext(invocation.Instance);

        private bool IsTicketCommand(IMethodSymbol method) =>
            method is { IsStatic: false, ReturnsVoid: true } &&
            Equal(method.ContainingType, context) &&
            ((method.Name, method.Arity, method.Parameters.Length) is ("Destroy", 0, 1) or ("Remove", 1, 1) ||
                IsAdditionCommand(method) && method.Parameters.Length == 2) &&
            method.Parameters[0].RefKind == RefKind.None &&
            IsEntityTicket(method.Parameters[0].Type);

        private bool IsAdditionCommand(IMethodSymbol method) =>
            method is { Name: "Add", IsStatic: false, Arity: 1, ReturnsVoid: true } &&
            Equal(method.ContainingType, context) && IsRuntimeSystemContext();

        private bool HasCreatedTicket(IInvocationOperation invocation) =>
            invocation.Arguments.Length != 0 &&
            invocation.Arguments[0] is { Parameter.Ordinal: 0, Value: ILocalReferenceOperation local } &&
            ticketLocals.Contains(local.Local);

        private bool IsTypedOutput(IMethodSymbol method) =>
            method is { Name: "Emit" or "Contribute", IsStatic: false, Arity: 1, ReturnsVoid: true } &&
            Equal(method.ContainingType, context) && IsRuntimeSystemContext();

        private void AnalyzeTypedOutput(IInvocationOperation invocation, Location location)
        {
            var method = invocation.TargetMethod;
            var type = method.TypeArguments[0];
            if (!IsEntryContext(invocation.Instance) || method.Parameters.Length != 1 ||
                method.Parameters[0].RefKind != RefKind.None || !Equal(method.Parameters[0].Type, type) ||
                invocation.Arguments.Length != 1 || invocation.Arguments[0].Parameter?.Ordinal != 0 ||
                !CanNameComponentType(type) || !CanPackOutput(type))
            {
                Report(Stream, location, $"Call {method.Name}<T> directly on the entry context with an accessible, portable payload.");
                return;
            }
            if (IsScalarResource(type))
                return;

            var value = invocation.Arguments[0].Value;
            while (true)
            {
                if (value is IConversionOperation { Conversion.IsIdentity: true } conversion)
                    value = conversion.Operand;
                else if (value is IParenthesizedOperation parentheses)
                    value = parentheses.Operand;
                else
                    break;
            }
            if (value is not (IDefaultValueOperation or IObjectCreationOperation) &&
                !(value is IParameterReferenceOperation parameter &&
                    entry.Parameters.Any(item => Equal(item, parameter.Parameter)) &&
                    parameter.Parameter.RefKind is RefKind.In or RefKind.Ref))
                Report(Stream, location, $"Use default, a plain field initializer, or an entry component as the {method.Name}<T> payload.");
        }

        private static bool IsPlainValueCreation(IOperation? operation) => operation is IObjectCreationOperation
        {
            Type: INamedTypeSymbol { TypeKind: TypeKind.Struct, IsUnmanagedType: true, IsRefLikeType: false, IsTupleType: false },
            Arguments.Length: 0,
            Constructor: null or { IsImplicitlyDeclared: true }
        };

        private bool IsPackedOutputInitializer(IOperation? operation) =>
            operation is IObjectOrCollectionInitializerOperation { Parent: IObjectCreationOperation creation } && IsPackedOutputValue(creation);

        private bool IsPackedOutputValue(IOperation operation) => operation.Parent switch
        {
            IArgumentOperation { Parent: IInvocationOperation invocation, Parameter: { } parameter } =>
                (IsAdditionCommand(invocation.TargetMethod) || IsTypedOutput(invocation.TargetMethod)) &&
                parameter.Ordinal == invocation.TargetMethod.Parameters.Length - 1,
            ISimpleAssignmentOperation assignment =>
                ReferenceEquals(operation, assignment.Value) && IsPackedOutputInitializer(assignment.Parent),
            IConversionOperation { Conversion.IsIdentity: true } conversion => IsPackedOutputValue(conversion),
            IParenthesizedOperation parentheses => IsPackedOutputValue(parentheses),
            _ => false
        };

        private bool CanPackOutput(ITypeSymbol type)
        {
            if (type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32 or SpecialType.System_UInt32 ||
                Equal(type, fixedType))
                return true;
            if (type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
                return (underlying.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32) && CanNameComponentType(type);
            return type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None,
                IsUnmanagedType: true, IsRefLikeType: false, IsTupleType: false } component &&
                !component.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() switch
                {
                    "System.Runtime.CompilerServices.InlineArrayAttribute" => true,
                    "System.Runtime.InteropServices.StructLayoutAttribute" =>
                        attribute.ConstructorArguments[0].Value is int layout && layout != 0,
                    _ => false
                }) &&
                component.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).All(field =>
                    !field.IsReadOnly && !field.IsImplicitlyDeclared && !field.IsFixedSizeBuffer && field.RefKind == RefKind.None &&
                    compilation.IsSymbolAccessibleWithin(field, compilation.Assembly) && CanPackOutput(field.Type));
        }

        private bool IsRemovalComponent(ITypeSymbol type) =>
            type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None,
                IsUnmanagedType: true, IsRefLikeType: false, IsTupleType: false } && CanNameComponentType(type);

        private bool CanNameComponentType(ITypeSymbol type) => type switch
        {
            IArrayTypeSymbol array => CanNameComponentType(array.ElementType),
            INamedTypeSymbol named => !named.IsFileLocal && !named.IsUnboundGenericType &&
                compilation.IsSymbolAccessibleWithin(named, compilation.Assembly) &&
                Equal(named.OriginalDefinition, compilation.GetTypeByMetadataName(MetadataName(named.OriginalDefinition))) &&
                (named.ContainingType == null || CanNameComponentType(named.ContainingType)) &&
                named.TypeArguments.All(CanNameComponentType),
            _ => false
        };

        private static string MetadataName(ISymbol symbol) =>
            symbol.ContainingType is { } parent ? MetadataName(parent) + "+" + symbol.MetadataName :
            symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? MetadataName(ns) + "." + symbol.MetadataName :
            symbol.MetadataName;

        private bool IsTicketUse(IOperation operation) => operation switch
        {
            IInvocationOperation invocation => IsTicketCreation(invocation) &&
                (invocation.Parent is IExpressionStatementOperation ||
                invocation.Parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation initializer } &&
                    ticketLocals.Contains(initializer.Symbol)),
            ILocalReferenceOperation local => ticketLocals.Contains(local.Local) &&
                local.Parent is IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation invocation } &&
                IsTicketCommand(invocation.TargetMethod),
            _ => false
        };

        private bool IsRuntimeSystemContext() =>
            context is { IsRefLikeType: true } && context.ContainingAssembly.Identity.Name == "SKECS.GA" &&
            context.DeclaringSyntaxReferences.Length == 0 &&
            Equal(context.ContainingAssembly, compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute")?.ContainingAssembly);

        private void ValidateSnapshotDeclaration()
        {
            foreach (var attribute in entry.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "GaSnapshotAttribute" } type ||
                    type.ContainingNamespace.ToDisplayString() != "SKECS.GA")
                    continue;

                if (IsRuntimeSystemContext() && Equal(type.ContainingAssembly, context?.ContainingAssembly) &&
                    type.DeclaringSyntaxReferences.Length == 0 &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0] is { Kind: TypedConstantKind.Array, IsNull: false } fields &&
                    fields.Values.Length > 0 &&
                    fields.Values.All(field => IsSnapshotScalar(field, "Bool") || IsSnapshotScalar(field, "Int32") ||
                        IsSnapshotScalar(field, "UInt32") || IsSnapshotScalar(field, "Fixed")))
                {
                    snapshotFields = fields.Values;
                }
                else
                {
                    var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? Source(entry);
                    Report(Unknown, location, "Declare a nonempty snapshot layout with the runtime GaSnapshot attribute and Bool, Int32, UInt32 or Fixed fields.");
                }
            }
        }

        private static bool IsSnapshotScalar(TypedConstant field, string name) =>
            field is { Kind: TypedConstantKind.Enum, Type: { } type } &&
            type.GetMembers(name).OfType<IFieldSymbol>().Any(member => member.HasConstantValue && object.Equals(member.ConstantValue, field.Value));

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

            var descriptor = method.Name is "Emit" or "Contribute" or "Create" or "Destroy" or "Remove" or "Add" or "SetDomainOrder" or "get_Commands" ? Stream : Unknown;
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
                if (IsResourcePath(argument.Value))
                {
                    Report(Write, argument.Syntax.GetLocation(), "Copy the tick resource value into a scalar local before passing it by reference.");
                    continue;
                }
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
                ITypeSymbol type when ownAccesses.TryGetValue(type, out var access) && access == OwnMode.ReadWrite => type,
                IMethodSymbol method when !Equal(method, entry) && !Equal(method.ContainingType, entry.ContainingType) &&
                    !method.IsReadOnly => method,
                _ => null
            };
        }

        private ISymbol? StorageRoot(IOperation? operation) => operation switch
        {
            ILocalReferenceOperation local when ownAliases.TryGetValue(local.Local, out var type) => type,
            ILocalReferenceOperation local => local.Local,
            IInvocationOperation own when TryOwnType(own, out var type) => type,
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
            IOperation? trailingStep = null;
            switch (loop)
            {
                case IForLoopOperation forLoop when forLoop.AtLoopBottom.Length == 1:
                    condition = forLoop.Condition;
                    step = forLoop.AtLoopBottom[0];
                    break;
                case IWhileLoopOperation { ConditionIsTop: true, ConditionIsUntil: false } whileLoop:
                    condition = whileLoop.Condition;
                    step = whileLoop.Body;
                    if (step is IBlockOperation block)
                    {
                        if (block.Operations.Length == 0)
                            return false;
                        step = block.Operations[0];
                        if (block.Operations.Length > 1 && !block.Descendants().OfType<IBranchOperation>().Any(branch =>
                            branch.BranchKind == BranchKind.Continue && Equal(branch.Target, whileLoop.ContinueLabel)))
                            trailingStep = block.Operations[block.Operations.Length - 1];
                    }
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

            int direction = GaIntRange.UnitStepDirection(step, counter.Local);
            if (direction == 0 && trailingStep is IExpressionStatementOperation trailing)
            {
                step = trailing.Operation;
                direction = GaIntRange.UnitStepDirection(step, counter.Local);
            }
            var comparisonMovesTowardLimit = (direction, compare.OperatorKind) switch
            {
                (1, BinaryOperatorKind.LessThan) or (-1, BinaryOperatorKind.GreaterThan) => true,
                (1, BinaryOperatorKind.LessThanOrEqual) or (-1, BinaryOperatorKind.GreaterThanOrEqual) =>
                    IsSafeInclusiveLimit(compare.RightOperand.ConstantValue, direction) ||
                    GaIntRange.TryGetFrozen(compare.RightOperand, loop, compilation, cancellationToken, out var range) &&
                    (direction > 0 ? range.Max < int.MaxValue : range.Min > int.MinValue) ||
                    UsesEnclosingIntCounter(loop, StorageRoot(compare.RightOperand)),
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

        // Like a strict comparison, an enclosing counter still needs the compiler's range proof.
        private static bool UsesEnclosingIntCounter(ILoopOperation loop, ISymbol? bound)
        {
            if (bound is not ILocalSymbol { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_Int32 } local)
                return false;

            for (IOperation? parent = loop.Parent; parent != null; parent = parent.Parent)
            {
                var condition = parent switch
                {
                    IForLoopOperation enclosing => enclosing.Condition,
                    IWhileLoopOperation { ConditionIsTop: true, ConditionIsUntil: false } enclosing => enclosing.Condition,
                    _ => null
                };
                if (condition is IBinaryOperation { LeftOperand: ILocalReferenceOperation counter } &&
                    Equal(counter.Local, local))
                    return true;
            }
            return false;
        }

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
