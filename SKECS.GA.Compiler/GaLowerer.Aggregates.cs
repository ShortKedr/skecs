using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA;

internal sealed partial class GaLowerer
{
    private sealed record BoundOperand(ITypeSymbol Type, GaValue? Scalar, Dictionary<string, GaSlot>? Fields);

    private Dictionary<ILocalSymbol, Dictionary<string, GaSlot>> CurrentAggregateLocals =>
        _helper?.AggregateLocals ?? _aggregateLocals;

    private static GaBlock BindingStatements(List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings) =>
        new(bindings.Select(binding => (GaStatement)new GaStore(binding.Slot, binding.Value)).ToArray());

    private static GaValue BindValues(List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings, GaValue value)
    {
        for (int i = bindings.Count - 1; i >= 0; i--)
        {
            var binding = bindings[i];
            value = new GaBoundValue(binding.Slot, binding.Value, value, binding.Source);
        }
        return value;
    }

    private Dictionary<string, GaSlot> CreateAggregate(ITypeSymbol type, IOperation site,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        var fields = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
        foreach (var (path, field, _) in AggregateFields(type, site))
        {
            var scalar = Scalar(field.Type, site);
            var local = AddLocal(scalar);
            fields.Add(path, local);
            bindings.Add((local, new GaConstant(scalar, 0, Source(site)), Source(site)));
        }
        return fields;
    }

    private IEnumerable<(string Path, IFieldSymbol Field, bool ReadOnly)> AggregateFields(ITypeSymbol type, IOperation site,
        string prefix = "", bool parentReadOnly = false)
    {
        if (type is not INamedTypeSymbol aggregate || !IsAggregate(type) ||
            GetInlineArrayDeclaration(aggregate) != null || aggregate.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
                attribute.ConstructorArguments is [{ Value: int layout }] && layout != 0))
            throw Unsupported(site, "Use a sequential value with explicit scalar fields.");
        foreach (var field in aggregate.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
            .OrderBy(field => field.MetadataName, StringComparer.Ordinal))
        {
            if (field.IsImplicitlyDeclared || field.IsFixedSizeBuffer || field.RefKind != RefKind.None ||
                field.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                throw Unsupported(site, "Use accessible explicit instance fields in a portable value.");
            string path = prefix + field.MetadataName;
            if (IsAggregate(field.Type))
            {
                foreach (var nested in AggregateFields(field.Type, site, path + ".", parentReadOnly || field.IsReadOnly))
                    yield return nested;
            }
            else
            {
                _ = Scalar(field.Type, site);
                yield return (path, field, parentReadOnly || field.IsReadOnly);
            }
        }
    }

    private Dictionary<string, GaSlot> AggregateDestination(IOperation operation)
    {
        var path = new Stack<string>();
        bool readOnly = false;
        var root = ReadArrayFieldPath(UnwrapIdentity(operation), path, ref readOnly);
        string prefix = path.Count == 0 ? "" : string.Join(".", path) + ".";
        var captured = FindCapturedFields(root);
        GaComponent? component = captured == null ? ResolveStorage(root, operation) : null;
        var destination = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
        foreach (var (fieldPath, field, fieldReadOnly) in AggregateFields(ResolveHelperType(operation.Type)!, operation))
        {
            GaSlot slot;
            if (captured != null)
            {
                if (!captured.TryGetValue(prefix + fieldPath, out slot))
                    throw Unsupported(operation, "The value field has no bound local storage.");
            }
            else
            {
                if (readOnly || fieldReadOnly || component!.Access != GaFieldAccess.ReadWrite)
                    throw Unsupported(operation, "The target value has a read-only component field.");
                slot = FieldSlot(component!, prefix + fieldPath, Scalar(field.Type, operation),
                    field.Type.TypeKind == TypeKind.Enum ? QualifiedName(field.Type) : null);
            }
            destination.Add(fieldPath, slot);
        }
        return destination;
    }

    private GaStatement StoreAggregate(ITypeSymbol type, Dictionary<string, GaSlot> destination, IOperation value)
    {
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        var copy = CaptureAggregate(ResolveHelperType(type)!, value, bindings);
        return new GaBlock([BindingStatements(bindings), CopyAggregate(destination, copy, Source(value))]);
    }

    private static GaBlock CopyAggregate(Dictionary<string, GaSlot> destination,
        Dictionary<string, GaSlot> copy, GaSourceSpan source) =>
        new(destination.Select(field => (GaStatement)new GaStore(field.Value, new GaLoad(copy[field.Key], source))).ToArray());

    private Dictionary<string, GaSlot> CaptureAggregate(ITypeSymbol type, IOperation operation,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        type = ResolveHelperType(type)!;
        operation = UnwrapIdentity(operation);
        if (FindInlineAccess(operation) is { } arrayAccess)
            return CaptureArrayValue(type, operation, arrayAccess, bindings);
        if (operation is IConversionOperation { OperatorMethod: null, Operand: IObjectCreationOperation or IConditionalOperation } targetTyped)
            return CaptureAggregate(type, targetTyped.Operand, bindings);
        if (operation is IConversionOperation { OperatorMethod: null, Operand: IDefaultValueOperation })
            return CreateAggregate(type, operation, bindings);
        if (operation is IDefaultValueOperation)
            return CreateAggregate(type, operation, bindings);
        if (operation is IObjectCreationOperation creation)
        {
            if (type is INamedTypeSymbol { StaticConstructors.IsEmpty: false })
                throw Unsupported(operation, "A GA constructor cannot depend on static initialization of its declaring type.");
            var fields = CreateAggregate(type, operation, bindings);
            if (creation.Constructor is { IsImplicitlyDeclared: false } constructor)
            {
                var effect = LowerAggregateConstructor(creation, constructor, creation.Arguments, fields);
                bindings.Add((AddLocal(GaScalar.Bool), effect, Source(creation)));
            }
            if (creation.Initializer != null)
            {
                var stores = new List<GaStatement>();
                foreach (var initializer in creation.Initializer.Initializers)
                {
                    if (initializer is not ISimpleAssignmentOperation
                        { IsRef: false, Target: IFieldReferenceOperation { Field.IsStatic: false } target } assignment)
                        throw Unsupported(initializer, "Initialize explicit instance fields of a portable value.");
                    var valueBindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
                    var fieldType = ResolveHelperType(target.Type)!;
                    if (IsAggregate(fieldType))
                    {
                        var nested = CaptureAggregate(fieldType, assignment.Value, valueBindings);
                        var destination = fields.Where(field => field.Key.StartsWith(target.Field.MetadataName + ".", StringComparison.Ordinal))
                            .ToDictionary(field => field.Key[(target.Field.MetadataName.Length + 1)..], field => field.Value, StringComparer.Ordinal);
                        stores.Add(BindingStatements(valueBindings));
                        stores.Add(CopyAggregate(destination, nested, Source(assignment)));
                    }
                    else
                    {
                        stores.Add(new GaStore(fields[target.Field.MetadataName], LowerValue(assignment.Value)));
                    }
                }
                AddAggregateEffect(bindings, new GaBlock(stores), operation);
            }
            return fields;
        }
        if (operation is IConditionalOperation { WhenFalse: { } other, IsRef: false } conditional)
        {
            var fields = CreateAggregate(type, operation, bindings);
            AddAggregateEffect(bindings, new GaIf(LowerValue(conditional.Condition),
                StoreAggregate(type, fields, conditional.WhenTrue), StoreAggregate(type, fields, other)), operation);
            return fields;
        }
        if (AggregateMember(operation) is { } member)
        {
            var fields = CreateAggregate(type, operation, bindings);
            var effect = LowerValueMember(operation, member.Method, member.Receiver, member.Arguments,
                ordinals: member.Ordinals, aggregateResult: fields);
            bindings.Add((AddLocal(GaScalar.Bool), effect, Source(operation)));
            return fields;
        }

        var result = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
        var path = new Stack<string>();
        bool readOnly = false;
        var root = ReadArrayFieldPath(operation, path, ref readOnly);
        string prefix = path.Count == 0 ? "" : string.Join(".", path) + ".";
        var captured = FindCapturedFields(root);
        GaComponent? component = null;
        bool optionalCopy = root is ILocalReferenceOperation localCopy && _optionalLocals.ContainsKey(localCopy.Local);
        if (captured == null && root != null && !ReferenceEquals(root, operation) &&
            (AggregateMember(root) != null || root is IObjectCreationOperation or IConditionalOperation))
            captured = CaptureAggregate(root.Type!, root, bindings);
        if (captured == null && !optionalCopy)
            component = ResolveStorage(root, operation);
        foreach (var (fieldPath, field, fieldReadOnly) in AggregateFields(type, operation))
        {
            var scalar = Scalar(field.Type, operation);
            string? enumType = field.Type.TypeKind == TypeKind.Enum ? QualifiedName(field.Type) : null;
            GaSlot source;
            if (captured != null && captured.TryGetValue(prefix + fieldPath, out source))
            {
            }
            else if (root is ILocalReferenceOperation optional && _optionalLocals.TryGetValue(optional.Local, out var copy))
            {
                source = OptionalFieldSlot(copy, prefix + fieldPath, scalar, enumType, operation);
            }
            else if (component != null)
            {
                source = FieldSlot(component, prefix + fieldPath, scalar, enumType,
                    readOnly || fieldReadOnly ? GaFieldAccess.Read : component.Access);
            }
            else
            {
                throw Unsupported(operation, "Capture a bound component, parameter or value local.");
            }
            var destination = AddLocal(scalar);
            result.Add(fieldPath, destination);
            bindings.Add((destination, new GaLoad(source, Source(operation)), Source(operation)));
        }
        return result;
    }

    private GaValue LowerAggregateConstructor(IOperation site, IMethodSymbol constructor,
        IReadOnlyList<IArgumentOperation> supplied, Dictionary<string, GaSlot> fields)
    {
        constructor = SpecializeMethod(constructor);
        var definition = constructor.OriginalDefinition;
        if (definition.DeclaringSyntaxReferences is not [var reference] ||
            reference.GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration ||
            constructor.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams) ||
            supplied.Count != constructor.Parameters.Length ||
            supplied.Any(argument => argument.Parameter == null || argument.ArgumentKind != ArgumentKind.Explicit))
            throw Unsupported(site, "Use a source-defined value constructor with explicit by-value arguments.");

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        if (model.GetOperation(declaration, cancellationToken) is not IConstructorBodyOperation body ||
            (body.BlockBody ?? body.ExpressionBody) is not { } block)
            throw Unsupported(site, "The GA value constructor must have a source-defined body.");

        var frame = new HelperFrame(new GaSlot[constructor.Parameters.Length],
            new Dictionary<ILocalSymbol, GaSlot>(SymbolEqualityComparer.Default), MemberTypes(constructor, site));
        frame.AggregateInputs.Add(ReceiverInput, fields);
        var arguments = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        foreach (var argument in supplied.OrderBy(argument => argument.Syntax.SpanStart))
        {
            int ordinal = argument.Parameter!.Ordinal;
            BindMemberInput(frame, ordinal, constructor.Parameters[ordinal].Type, argument.Value, arguments);
        }

        return LowerAggregateHelperBody(site, definition, declaration, block.Operations, null, frame, arguments,
            initialize: Initialize);

        GaBlock Initialize()
        {
            if (declaration.Initializer != null)
            {
                var initializer = model.GetOperation(declaration.Initializer, cancellationToken) as IInvocationOperation ??
                    (body.Initializer as IExpressionStatementOperation)?.Operation as IInvocationOperation;
                if (!declaration.Initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword) ||
                    initializer == null)
                    throw Unsupported(site, "Use a source-defined struct constructor or the default this() initializer.");
                if (!initializer.TargetMethod.IsImplicitlyDeclared)
                {
                    var effect = LowerAggregateConstructor(initializer, initializer.TargetMethod, initializer.Arguments, fields);
                    return new GaBlock([new GaStore(AddLocal(GaScalar.Bool), effect)]);
                }
            }
            return LowerInstanceFieldInitializers(constructor.ContainingType, fields, site);
        }
    }

    private GaBlock LowerInstanceFieldInitializers(INamedTypeSymbol type, Dictionary<string, GaSlot> fields, IOperation site)
    {
        var declarations = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
            .SelectMany(field => field.DeclaringSyntaxReferences.Select(reference =>
                (Field: field, Syntax: reference.GetSyntax(cancellationToken) as VariableDeclaratorSyntax)))
            .Where(item => item.Syntax?.Initializer != null)
            .ToLookup(item => item.Syntax!.SyntaxTree);
        var stores = new List<GaStatement>();
        var sourceText = new System.Text.StringBuilder();
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var (field, syntax) in declarations[tree].OrderBy(item => item.Syntax!.SpanStart))
            {
                var initializer = syntax!.Initializer!;
                var model = compilation.GetSemanticModel(tree);
                if (model.GetOperation(initializer, cancellationToken) is not IFieldInitializerOperation operation)
                    throw Unsupported(site, "The GA instance field initializer must have a source operation.");

                string name = field.MetadataName;
                if (IsAggregate(field.Type))
                {
                    string prefix = name + ".";
                    var destination = fields.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                        .ToDictionary(item => item.Key[prefix.Length..], item => item.Value, StringComparer.Ordinal);
                    stores.Add(StoreAggregate(field.Type, destination, operation.Value));
                }
                else if (fields.TryGetValue(name, out var slot))
                {
                    stores.Add(new GaStore(slot, LowerValue(operation.Value)));
                }
                else
                {
                    throw Unsupported(operation, "Initialize only explicit portable instance fields.");
                }
                sourceText.Append(GaCompilerText.NormalizeLineEndings(syntax.ToFullString())).Append('\n');
            }
        }
        if (stores.Count != 0)
            _externalHelperSources.Add(type.OriginalDefinition.GetDocumentationCommentId() + "\n" + sourceText);
        return new GaBlock(stores);
    }

    private (IMethodSymbol Method, IOperation? Receiver, IOperation[] Arguments, int[]? Ordinals)? AggregateMember(IOperation operation)
    {
        return operation switch
        {
            IConversionOperation { OperatorMethod: { } method } conversion => (method, null, [conversion.Operand], null),
            IUnaryOperation { IsLifted: false, OperatorMethod: { } method } unary => (method, null, [unary.Operand], null),
            IBinaryOperation { IsLifted: false, OperatorMethod: { } method } binary => (method, null, [binary.LeftOperand, binary.RightOperand], null),
            IPropertyReferenceOperation { Property.GetMethod: { } getter } property =>
                (getter, property.Instance, property.Arguments.OrderBy(argument => argument.Syntax.SpanStart).Select(argument => argument.Value).ToArray(),
                 property.Arguments.OrderBy(argument => argument.Syntax.SpanStart).Select(argument => argument.Parameter!.Ordinal).ToArray()),
            IInvocationOperation { TargetMethod: { IsStatic: true } method } invocation =>
                (method, null, invocation.Arguments.OrderBy(argument => argument.Syntax.SpanStart).Select(argument => argument.Value).ToArray(),
                 invocation.Arguments.OrderBy(argument => argument.Syntax.SpanStart).Select(argument => argument.Parameter!.Ordinal).ToArray()),
            _ => null
        };
    }

    private void AddAggregateEffect(List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings,
        GaStatement effect, IOperation site) =>
        bindings.Add((AddLocal(GaScalar.Bool), new GaBlockValue(effect, new GaConstant(GaScalar.Bool, 0, Source(site)), Source(site)), Source(site)));

    private GaValue? LowerAggregateProjection(IFieldReferenceOperation field)
    {
        var path = new Stack<string>();
        bool readOnly = false;
        var root = ReadArrayFieldPath(field, path, ref readOnly);
        if (root?.Type == null || root is IParameterReferenceOperation or ILocalReferenceOperation or IInstanceReferenceOperation ||
            root is IInvocationOperation { TargetMethod.Name: "Own" } own && IsContextReceiver(own) ||
            !IsAggregate(root.Type))
            return null;
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        var fields = CaptureAggregate(root.Type, root, bindings);
        return BindValues(bindings, new GaLoad(fields[string.Join(".", path)], Source(field)));
    }

    private GaValue LowerAggregateHelperBody(IOperation site, IMethodSymbol definition, SyntaxNode declaration,
        IReadOnlyList<IOperation> operations, IOperation? expression, HelperFrame frame,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings, ITypeSymbol? returnType = null,
        Dictionary<string, GaSlot>? result = null, Func<GaBlock>? initialize = null)
    {
        if (!_activeHelpers.Add(definition))
            throw Unsupported(site, "Recursive GA helpers and value members are not supported.");
        var previous = _helper;
        var previousReturn = _helperReturn;
        var outerControl = _loopControl;
        int outerDepth = _helperLoopBaseDepth;
        var completed = AddLocal(GaScalar.Bool);
        var state = new HelperReturn(completed, completed)
        {
            AggregateType = returnType,
            AggregateResult = result,
            IsConstructor = definition.MethodKind == MethodKind.Constructor
        };
        _helper = frame;
        _helperReturn = state;
        _loopControl = null;
        _helperLoopBaseDepth = _loopCounters.Count;
        try
        {
            var initialization = initialize?.Invoke();
            GaStatement body = expression == null ? LowerBlock(operations) : StoreAggregate(returnType!, result!, expression);
            if (initialization is { Statements.Count: > 0 })
                body = new GaBlock([initialization, body]);
            GaValue effect = new GaBlockValue(body, new GaConstant(GaScalar.Bool, 0, Source(site)), Source(site));
            foreach (var local in frame.Locals.Values.Reverse())
                effect = new GaBoundValue(local, new GaConstant(local.Type, 0, Source(site)), effect, Source(site));
            effect = new GaBoundValue(completed, new GaConstant(GaScalar.Bool, 0, Source(site)), effect, Source(site));
            if (!SymbolEqualityComparer.Default.Equals(definition.ContainingType, entry.ContainingType))
                _externalHelperSources.Add(definition.GetDocumentationCommentId() + "\n" +
                    GaCompilerText.NormalizeLineEndings(declaration.ToFullString()));
            return BindValues(bindings, effect);
        }
        finally
        {
            _helper = previous;
            _helperReturn = previousReturn;
            _loopControl = outerControl;
            _helperLoopBaseDepth = outerDepth;
            _activeHelpers.Remove(definition);
        }
    }

    private BoundOperand CaptureOperand(ITypeSymbol type, IOperation operation,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings) => IsAggregate(type)
        ? new BoundOperand(type, null, CaptureAggregate(type, operation, bindings))
        : BindScalarOperand(type, LowerValue(operation), operation, bindings);

    private BoundOperand BindScalarOperand(ITypeSymbol type, GaValue value, IOperation site,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        var local = AddLocal(value.Type);
        bindings.Add((local, value, Source(site)));
        return new BoundOperand(type, new GaLoad(local, Source(site)), null);
    }

    private void BindOperand(HelperFrame frame, int index, ITypeSymbol type, BoundOperand operand,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings, IOperation site)
    {
        if (!SymbolEqualityComparer.Default.Equals(type, operand.Type))
            throw Unsupported(site, "The selected member argument type does not match its bound value.");
        if (operand.Fields != null)
        {
            var copy = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
            foreach (var (path, source) in operand.Fields)
            {
                var local = AddLocal(source.Type);
                copy.Add(path, local);
                bindings.Add((local, new GaLoad(source, Source(site)), Source(site)));
            }
            frame.AggregateInputs.Add(index, copy);
        }
        else
        {
            var local = AddLocal(operand.Scalar!.Type);
            frame.Parameters[index] = local;
            bindings.Add((local, operand.Scalar, Source(site)));
        }
    }

    private BoundOperand CallOperand(IMethodSymbol method, IReadOnlyList<BoundOperand> inputs, IOperation site,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (IsAggregate(method.ReturnType))
        {
            var fields = CreateAggregate(method.ReturnType, site, bindings);
            var effect = LowerValueMember(site, method, null, [], aggregateResult: fields, operands: inputs);
            bindings.Add((AddLocal(GaScalar.Bool), effect, Source(site)));
            return new BoundOperand(method.ReturnType, null, fields);
        }
        return BindScalarOperand(method.ReturnType, LowerValueMember(site, method, null, [], operands: inputs), site, bindings);
    }

    private BoundOperand ConvertOperand(BoundOperand operand, ITypeSymbol target, CommonConversion conversion,
        IOperation site, bool isChecked, List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (conversion.MethodSymbol is { } method)
        {
            var input = StandardOperandConversion(operand, method.Parameters[0].Type, site, isChecked, bindings);
            var result = CallOperand(method, [input], site, bindings);
            return StandardOperandConversion(result, target, site, isChecked, bindings);
        }
        return StandardOperandConversion(operand, target, site, isChecked, bindings);
    }

    private BoundOperand StandardOperandConversion(BoundOperand operand, ITypeSymbol target, IOperation site, bool isChecked,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (SymbolEqualityComparer.Default.Equals(operand.Type, target))
            return operand;
        var conversion = compilation.ClassifyConversion(operand.Type, target);
        if (operand.Scalar == null || !(conversion.IsIdentity || conversion.IsNumeric || conversion.IsEnumeration))
            throw Unsupported(site, "This compound conversion has no portable standard conversion.");
        var type = Scalar(target, site);
        GaValue value = type == operand.Scalar.Type ? operand.Scalar :
            new GaCast(type, operand.Scalar, isChecked, Source(site));
        return BindScalarOperand(target, value, site, bindings);
    }

    private BoundOperand CompoundOperand(ICompoundAssignmentOperation compound, BoundOperand old,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (compound.OperatorMethod is not { } method)
            throw Unsupported(compound, "This promoted compound operator has no portable lowering.");
        var left = ConvertOperand(old, method.Parameters[0].Type, compound.InConversion, compound, compound.IsChecked, bindings);
        var right = CaptureOperand(method.Parameters[1].Type, compound.Value, bindings);
        var result = CallOperand(method, [left, right], compound, bindings);
        return ConvertOperand(result, compound.Target.Type!, compound.OutConversion, compound, compound.IsChecked, bindings);
    }

    private GaValue LowerConvertedCompound(ICompoundAssignmentOperation compound, GaValue oldValue)
    {
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        var old = BindScalarOperand(compound.Target.Type!, oldValue, compound.Target, bindings);
        var result = CompoundOperand(compound, old, bindings);
        return BindValues(bindings, result.Scalar!);
    }

    private GaStatement LowerAggregateCompound(ICompoundAssignmentOperation compound)
    {
        var destination = AggregateDestination(compound.Target);
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        var old = CaptureOperand(compound.Target.Type!, compound.Target, bindings);
        var result = CompoundOperand(compound, old, bindings);
        return new GaBlock([BindingStatements(bindings), CopyAggregate(destination, result.Fields!, Source(compound))]);
    }

    private Dictionary<ITypeParameterSymbol, ITypeSymbol> MemberTypes(IMethodSymbol target, IOperation site)
    {
        var types = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        Bind(target.OriginalDefinition.TypeParameters, target.TypeArguments);
        for (var owner = target.ContainingType; owner != null; owner = owner.ContainingType)
            Bind(owner.OriginalDefinition.TypeParameters, owner.TypeArguments);
        return types;

        void Bind(System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> parameters,
            System.Collections.Immutable.ImmutableArray<ITypeSymbol> arguments)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var type = ResolveHelperType(arguments[i])!;
                if (IsAggregate(type))
                    _ = AggregateFields(type, site).ToArray();
                else
                    _ = Scalar(type, site);
                types.Add(parameters[i], type);
            }
        }
    }

    private IMethodSymbol SpecializeMethod(IMethodSymbol method)
    {
        if (_helper == null)
            return method;
        var owner = (INamedTypeSymbol)ResolveHelperType(method.ContainingType)!;
        IMethodSymbol bound = method;
        if (!SymbolEqualityComparer.Default.Equals(owner, method.ContainingType))
        {
            if (method.AssociatedSymbol is IPropertySymbol property)
            {
                var selected = owner.GetMembers(property.Name).OfType<IPropertySymbol>()
                    .Single(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, property.OriginalDefinition));
                bound = selected.GetMethod!;
            }
            else
            {
                bound = owner.GetMembers(method.Name).OfType<IMethodSymbol>()
                    .Single(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, method.OriginalDefinition));
            }
        }
        if (method.IsGenericMethod)
        {
            var arguments = method.TypeArguments.Select(argument => ResolveHelperType(argument)!).ToArray();
            bound = bound.ConstructedFrom.Construct(arguments);
        }
        return bound;
    }
}
