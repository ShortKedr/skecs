using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA;

internal sealed class GaLowerer(CSharpCompilation compilation, IMethodSymbol entry, CancellationToken cancellationToken)
{
    private readonly Dictionary<IParameterSymbol, GaComponent> _components = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, (GaComponent Component, int Index)> _declaredAccesses = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ILocalSymbol, GaComponent> _ownAliases = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ILocalSymbol, OptionalCopy> _optionalLocals = new(SymbolEqualityComparer.Default);
    private readonly List<GaStatement> _optionalDefaults = new();
    private readonly Dictionary<(string Component, string Path), GaSlot> _fields = new();
    private readonly Dictionary<(string Component, string Path), GaArrayField> _arrayFields = new();
    private readonly Dictionary<ILocalSymbol, GaSlot> _locals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ILocalSymbol, Dictionary<string, GaSlot>> _aggregateLocals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ILocalSymbol, int> _ticketLocals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, int> _removalComponentIndices = new(StringComparer.Ordinal);
    private readonly List<string> _removalComponents = new();
    private readonly Dictionary<string, int> _additionComponentIndices = new(StringComparer.Ordinal);
    private readonly List<GaAdditionComponent> _additionComponents = new();
    private readonly Dictionary<(string Id, bool IsContribution), int> _eventTypeIndices = new();
    private readonly List<GaEventType> _eventTypes = new();
    private readonly List<GaField> _schema = new();
    private readonly List<GaTickResourceField> _resourceFields = new();
    private readonly Dictionary<string, int> _resourceFieldIndices = new(StringComparer.Ordinal);
    private readonly HashSet<ILocalSymbol> _resourceAliases = new(SymbolEqualityComparer.Default);
    private readonly List<GaBufferField> _bufferFields = new();
    private readonly Dictionary<string, int> _bufferFieldIndices = new(StringComparer.Ordinal);
    private readonly List<GaScalar> _localTypes = new();
    private readonly HashSet<string> _externalHelperSources = new(StringComparer.Ordinal);
    private readonly HashSet<IMethodSymbol> _activeHelpers = new(SymbolEqualityComparer.Default);
    private HelperFrame? _helper;
    private readonly List<GaStore> _scalarDefaults = new();
    private IParameterSymbol? _context;
    private GaSnapshotLayout? _snapshotLayout;
    private INamedTypeSymbol? _resourceType;
    private INamedTypeSymbol? _bufferType;
    private bool _insideLoop;
    private bool _hasLoopBranches;
    private LoopControl? _loopControl;

    private sealed record HelperFrame(GaSlot[] Parameters, Dictionary<ILocalSymbol, GaSlot> Locals,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> Types)
    {
        internal Dictionary<int, Dictionary<string, GaSlot>> AggregateInputs { get; } = new();
    }

    private const int ReceiverInput = -1;

    private sealed record LoopControl(ILabelSymbol Exit, ILabelSymbol Continue, GaSlot State);
    private const uint LoopRunning = 0;
    private const uint LoopContinue = 1;
    private const uint LoopBreak = 2;

    internal GaProgram Lower(string systemId, string sourceDigest, IOperation body)
    {
        _snapshotLayout = ReadSnapshotLayout();
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in entry.Parameters)
        {
            if (IsSystemContext(parameter.Type))
            {
                if (_context != null || parameter.RefKind != RefKind.In)
                    throw new GaLoweringException(parameter.Locations[0], "Declare a single in SystemContext parameter.");
                _context = parameter;
                continue;
            }
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
        var components = entry.Parameters.Where(_components.ContainsKey).Select(parameter => _components[parameter]).ToList();
        ReadDeclaredAccesses(componentIds, components);
        _resourceType = ReadDeclaredInput("GaTickResource", "runner tick input");
        _bufferType = ReadDeclaredInput("GaReadBuffer", "read buffer element type");
        FindOptionalLocals(body);
        var lowered = LowerStatement(body);
        // The guarded tail excludes paths that exit a loop body. Roslyn already proved
        // definite assignment on the source paths, so these defaults are unobservable.
        var root = _hasLoopBranches
            ? new GaBlock([.. _optionalDefaults, .. _scalarDefaults, lowered])
            : new GaBlock([.. _optionalDefaults, lowered]);
        if (_externalHelperSources.Count != 0)
        {
            string helpers = string.Join("\n", _externalHelperSources.OrderBy(source => source, StringComparer.Ordinal));
            sourceDigest = GaArtifactManifest.ComputeDigest(Encoding.UTF8.GetBytes(sourceDigest + "\n" + helpers));
        }
        try
        {
            return new GaProgram(systemId, sourceDigest, components.ToArray(), _schema.ToArray(), _localTypes.ToArray(),
                root, _ticketLocals.Count, _removalComponents.ToArray(), _additionComponents.ToArray(), _snapshotLayout,
                _resourceType == null ? null : QualifiedName(_resourceType), _resourceFields.ToArray(),
                _bufferType == null ? null : QualifiedName(_bufferType), _bufferFields.ToArray(), _eventTypes.ToArray());
        }
        catch (GaEventCapacityException error)
        {
            throw Unsupported(body, error.Message);
        }
    }

    private INamedTypeSymbol? ReadDeclaredInput(string attribute, string description)
    {
        INamedTypeSymbol? element = null;
        foreach (var declaration in entry.GetAttributes())
        {
            if (declaration.AttributeClass is not { } type || QualifiedName(type) != $"global::SKECS.GA.{attribute}Attribute")
                continue;

            var location = declaration.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? entry.Locations[0];
            if (element != null || _context == null || type.DeclaringSyntaxReferences.Length != 0 ||
                !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _context.Type.ContainingAssembly) ||
                declaration.ConstructorArguments is not [{ Kind: TypedConstantKind.Type, Value: INamedTypeSymbol input }] ||
                !(IsOwnComponent(input) || IsScalarResource(input)))
                throw new GaLoweringException(location, $"Declare one portable {description} with the runtime {attribute} attribute.");
            element = input;
        }
        return element;
    }

    private static bool IsScalarResource(ITypeSymbol type) => IsFixed(type) ||
        type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32 or SpecialType.System_UInt32 ||
        type is INamedTypeSymbol
        {
            IsFileLocal: false,
            EnumUnderlyingType.SpecialType: SpecialType.System_Int32 or SpecialType.System_UInt32
        };

    private void ReadDeclaredAccesses(HashSet<string> componentIds, List<GaComponent> components)
    {
        foreach (var declaration in entry.GetAttributes())
        {
            if (declaration.AttributeClass is not { } type || QualifiedName(type) != "global::SKECS.GA.GaAccessAttribute")
                continue;

            var syntax = declaration.ApplicationSyntaxReference?.GetSyntax();
            var location = syntax?.GetLocation() ?? entry.Locations[0];
            if (_context == null || type.DeclaringSyntaxReferences.Length != 0 ||
                !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _context.Type.ContainingAssembly) ||
                declaration.ConstructorArguments is not [
                    { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol component },
                    { Kind: TypedConstantKind.Enum, Value: int mode }] ||
                !IsOwnComponent(component) || !Enum.IsDefined((GaOwnAccess)mode))
                throw new GaLoweringException(location, "Declare a portable own component and access mode with the runtime GaAccess attribute.");

            string id = QualifiedName(component);
            if (!componentIds.Add(id))
                throw new GaLoweringException(location, "Declare each component once, either as a query parameter or with GaAccess.");

            var access = (GaOwnAccess)mode;
            var binding = new GaComponent(id, access == GaOwnAccess.ReadWrite ? GaFieldAccess.ReadWrite : GaFieldAccess.Read)
            {
                Origin = access == GaOwnAccess.OptionalRead ? GaComponentOrigin.Optional : GaComponentOrigin.Own,
                OwnSource = syntax is null ? default : Source(syntax)
            };
            _declaredAccesses.Add(component, (binding, components.Count));
            components.Add(binding);
        }
    }

    private void FindOptionalLocals(IOperation body)
    {
        foreach (var operation in body.Descendants().OfType<IInvocationOperation>())
        {
            if (operation.TargetMethod.Name != "TryRead" || !IsSystemContext(operation.TargetMethod.ContainingType) ||
                !ValidContextCall(operation, argumentCount: 1) ||
                !_declaredAccesses.TryGetValue(operation.TargetMethod.TypeArguments[0], out var binding) ||
                binding.Component.Origin != GaComponentOrigin.Optional ||
                OptionalOutTarget(operation) is not ILocalReferenceOperation { Local.RefKind: RefKind.None } target)
                continue;

            _optionalLocals.TryAdd(target.Local, new OptionalCopy(binding.Component));
        }
    }

    private sealed class OptionalCopy(GaComponent component)
    {
        internal GaComponent Component { get; } = component;
        internal Dictionary<string, GaSlot> Locals { get; } = new(StringComparer.Ordinal);
        internal List<GaOptionalFieldCopy> Copies { get; } = new();
        internal List<GaStatement> Defaults { get; } = new();
    }

    private GaSnapshotLayout? ReadSnapshotLayout()
    {
        var declarations = entry.GetAttributes()
            .Where(attribute => attribute.AttributeClass is { } type &&
                QualifiedName(type) == "global::SKECS.GA.GaSnapshotAttribute")
            .ToArray();
        if (declarations.Length == 0)
            return null;

        var declaration = declarations[0];
        var type = declaration.AttributeClass!;
        var marker = entry.ContainingType.GetAttributes()
            .Select(attribute => attribute.AttributeClass)
            .FirstOrDefault(attribute => attribute is not null &&
                QualifiedName(attribute) == "global::SKECS.GA.SecsSystemAttribute");
        var location = declaration.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? entry.Locations[0];
        if (declarations.Length != 1 || type.DeclaringSyntaxReferences.Length != 0 ||
            type.ContainingAssembly.Identity.Name != "SKECS.GA" ||
            !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, marker?.ContainingAssembly) ||
            declaration.ConstructorArguments is not [{ Kind: TypedConstantKind.Array, IsNull: false } layout] ||
            layout.Values.Length == 0)
            throw new GaLoweringException(location, "Declare one nonempty snapshot layout with the runtime GaSnapshot attribute.");

        var fields = new GaScalar[layout.Values.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            if (layout.Values[index] is not { Kind: TypedConstantKind.Enum, Value: int value } || !Enum.IsDefined((GaScalar)value))
                throw new GaLoweringException(location, "A snapshot layout field must be Bool, Int32, UInt32 or Fixed.");
            fields[index] = (GaScalar)value;
        }
        return new GaSnapshotLayout(fields);
    }

    private GaStatement LowerStatement(IOperation operation)
    {
        switch (operation)
        {
            case IBlockOperation block:
                return LowerBlock(block.Operations);
            case IExpressionStatementOperation expression:
                return LowerStatement(expression.Operation);
            case IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType) &&
                invocation.TargetMethod.Name is ("Has" or "TryRead"):
                return new GaStore(AddLocal(GaScalar.Bool), LowerOptionalAccess(invocation));
            case IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType) &&
                invocation.TargetMethod.Name is ("ReadBuffer" or "BufferLength"):
                var bufferValue = LowerValue(invocation);
                return new GaStore(AddLocal(bufferValue.Type), bufferValue);
            case IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType):
                return LowerContextStatement(invocation);
            case IEmptyOperation:
                return new GaBlock(Array.Empty<GaStatement>());
            case IVariableDeclarationGroupOperation group:
                return new GaBlock(group.Declarations.Select(LowerStatement).ToArray());
            case IVariableDeclarationOperation declaration:
                return new GaBlock(declaration.Declarators.Select(LowerStatement).ToArray());
            case IVariableDeclaratorOperation declarator:
                if (declarator.Symbol.RefKind != RefKind.None)
                {
                    if (declarator.Symbol.RefKind == RefKind.RefReadOnly &&
                        Unwrap(declarator.Initializer?.Value) is IInvocationOperation resource && IsResourceCall(resource) &&
                        SymbolEqualityComparer.Default.Equals(declarator.Symbol.Type, _resourceType))
                    {
                        _resourceAliases.Add(declarator.Symbol);
                        return new GaBlock([]);
                    }
                    if (declarator.Symbol.RefKind != RefKind.Ref ||
                        Unwrap(declarator.Initializer?.Value) is not IInvocationOperation own ||
                        !SymbolEqualityComparer.Default.Equals(declarator.Symbol.Type, own.Type))
                        throw Unsupported(declarator, "Use a direct ref local for Own<T>() or ref readonly local for Resource<T>().");
                    _ownAliases.Add(declarator.Symbol, ResolveOwn(own));
                    return new GaBlock([]);
                }
                if (_optionalLocals.TryGetValue(declarator.Symbol, out var optional))
                {
                    if (declarator.Initializer != null && Unwrap(declarator.Initializer.Value) is not IDefaultValueOperation)
                        throw Unsupported(declarator, "Initialize a TryRead<T> destination with default or leave it uninitialized.");
                    return new GaBlock(declarator.Initializer is null ? [] : optional.Defaults.AsReadOnly());
                }
                if (IsEntityTicket(declarator.Symbol.Type))
                {
                    if (declarator.Initializer?.Value is not IInvocationOperation creation || !IsCreateCall(creation))
                        throw Unsupported(declarator, "Initialize a GA ticket local directly with the entry context's Create().");
                    int ticketSlot = _ticketLocals.Count;
                    _ticketLocals.Add(declarator.Symbol, ticketSlot);
                    return new GaCreate(ticketSlot);
                }
                if (IsAggregate(declarator.Symbol.Type))
                {
                    if (declarator.Initializer?.Value is not { } initializer)
                        throw Unsupported(declarator, "Initialize the value copy from a declared component or existing copy.");
                    var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
                    _aggregateLocals.Add(declarator.Symbol, CaptureAggregate(declarator.Symbol.Type, initializer, bindings));
                    return new GaBlock(bindings.Select(binding => (GaStatement)new GaStore(binding.Slot, binding.Value)).ToArray());
                }
                var type = Scalar(declarator.Symbol.Type, declarator);
                var local = AddLocal(type);
                _locals.Add(declarator.Symbol, local);
                if (declarator.Initializer != null)
                    return new GaStore(local, LowerValue(declarator.Initializer.Value));
                _scalarDefaults.Add(new GaStore(local, new GaConstant(type, 0, Source(declarator))));
                return new GaBlock([]);
            case ISimpleAssignmentOperation assignment when !assignment.IsRef:
                if (FindInlineAccess(assignment.Target) is { } assignmentAccess)
                {
                    var array = LowerArrayAccess(assignment.Target, assignmentAccess, writable: true);
                    return new GaArrayStore(array.Field, array.Index, null, LowerValue(assignment.Value), array.Source);
                }
                return new GaStore(WritableSlot(assignment.Target), LowerValue(assignment.Value));
            case ICompoundAssignmentOperation compound when IsBuiltinOrFixed(compound.OperatorMethod) &&
                !compound.IsLifted && compound.InConversion.IsIdentity && compound.OutConversion.IsIdentity:
                if (FindInlineAccess(compound.Target) is { } compoundAccess)
                {
                    var array = LowerArrayAccess(compound.Target, compoundAccess, writable: true);
                    var previous = AddLocal(array.Type);
                    return new GaArrayStore(array.Field, array.Index, previous, LowerCompound(compound, previous), array.Source);
                }
                var slot = WritableSlot(compound.Target);
                return new GaStore(slot, LowerCompound(compound, slot));
            case IIncrementOrDecrementOperation change when change.OperatorMethod == null && !change.IsLifted:
                if (FindInlineAccess(change.Target) is { } incrementAccess)
                {
                    var array = LowerArrayAccess(change.Target, incrementAccess, writable: true);
                    var previous = AddLocal(array.Type);
                    return new GaArrayStore(array.Field, array.Index, previous, LowerIncrement(change, previous), array.Source);
                }
                var counter = WritableSlot(change.Target);
                return new GaStore(counter, LowerIncrement(change, counter));
            case IConditionalOperation branch when branch.Type == null:
                return new GaIf(LowerValue(branch.Condition), LowerStatement(branch.WhenTrue),
                    branch.WhenFalse == null ? null : LowerStatement(branch.WhenFalse));
            case IForLoopOperation loop:
                return LowerFor(loop);
            case IBranchOperation branch when LoopBranch(branch) is { } state:
                return SetLoopState(state, Source(branch));
            case ISwitchOperation match:
                return LowerSwitch(match);
            default:
                throw Unsupported(operation);
        }
    }

    private uint? LoopBranch(IBranchOperation branch)
    {
        if (_loopControl is not { } control)
            return null;
        return branch.BranchKind switch
        {
            BranchKind.Break when SymbolEqualityComparer.Default.Equals(branch.Target, control.Exit) => LoopBreak,
            BranchKind.Continue when SymbolEqualityComparer.Default.Equals(branch.Target, control.Continue) => LoopContinue,
            _ => null
        };
    }

    private GaStore SetLoopState(uint state, GaSourceSpan source) =>
        new(_loopControl!.State, new GaConstant(GaScalar.UInt32, state, source));

    private GaValue TestLoopState(uint state, GaBinaryOperator op, GaSourceSpan source) =>
        new GaBinary(GaScalar.Bool, op, new GaLoad(_loopControl!.State, source),
            new GaConstant(GaScalar.UInt32, state, source), false, source);

    private GaBlock LowerBlock(IReadOnlyList<IOperation> operations, ILabelSymbol? switchExit = null)
    {
        var statements = new List<GaStatement>(operations.Count);
        var tails = new Stack<(List<GaStatement> Before, GaValue Condition)>();
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            if (switchExit != null && i == operations.Count - 1)
            {
                if (operation is IBranchOperation { BranchKind: BranchKind.Break } branch &&
                    SymbolEqualityComparer.Default.Equals(branch.Target, switchExit))
                    break;
                if (operation is IBlockOperation block)
                {
                    statements.Add(LowerBlock(block.Operations, switchExit));
                    break;
                }
            }
            statements.Add(operation is IWhileLoopOperation loop
                ? LowerWhile(loop, i == 0 ? null : operations[i - 1])
                : LowerStatement(operation));
            if (_loopControl != null && i < operations.Count - 1 &&
                operation.DescendantsAndSelf().OfType<IBranchOperation>().Any(branch => LoopBranch(branch).HasValue))
            {
                // Keep declarations and their uses together in the guarded tail.
                tails.Push((statements, TestLoopState(LoopRunning, GaBinaryOperator.Equal, Source(operation))));
                statements = [];
            }
        }
        while (tails.TryPop(out var tail))
        {
            if (statements.Count != 0)
                tail.Before.Add(new GaIf(tail.Condition, new GaBlock(statements), null));
            statements = tail.Before;
        }
        return new GaBlock(statements);
    }

    private GaValue LowerCompound(ICompoundAssignmentOperation compound, GaSlot previous) =>
        Binary(previous.Type, compound.OperatorKind, new GaLoad(previous, Source(compound.Target)),
            LowerValue(compound.Value), compound.IsChecked, compound);

    private GaValue LowerIncrement(IIncrementOrDecrementOperation change, GaSlot previous)
    {
        if (previous.Type == GaScalar.Bool || change.Target.Type?.TypeKind == TypeKind.Enum)
            throw Unsupported(change);
        var source = Source(change);
        return new GaBinary(previous.Type,
            change.Kind == OperationKind.Increment ? GaBinaryOperator.Add : GaBinaryOperator.Subtract,
            new GaLoad(previous, Source(change.Target)), new GaConstant(previous.Type, 1, source), change.IsChecked, source);
    }

    private GaStatement LowerContextStatement(IInvocationOperation invocation)
    {
        if (IsCreateCall(invocation))
            return new GaCreate(-1);

        var method = invocation.TargetMethod;
        if (!IsContextReceiver(invocation) || method is not { IsStatic: false, ReturnsVoid: true })
            throw Unsupported(invocation);
        if (method is { Name: "Remove", Arity: 1 })
            return LowerRemove(invocation);
        if (method is { Name: "Add", Arity: 1 })
            return LowerAdd(invocation);
        if (method is { Name: "Emit" or "Contribute", Arity: 1 })
            return LowerTypedOutput(invocation, method.Name == "Contribute");
        if (method.Arity != 0)
            throw Unsupported(invocation);
        if (method.Name == "Destroy" && HasTicketParameter(method))
            return new GaDestroyTicket(TicketSlot(invocation));
        if (!HasInt32Parameters(method))
            throw Unsupported(invocation);

        return (method.Name, method.Parameters.Length, invocation.Arguments) switch
        {
            ("Emit", 1, [{ Parameter.Ordinal: 0 } value]) => new GaEmit(LowerValue(value.Value)),
            ("SetDomainOrder", 2, [{ Parameter.Ordinal: 0 } primary, { Parameter.Ordinal: 1 } secondary]) =>
                new GaSetDomainOrder(LowerValue(primary.Value), LowerValue(secondary.Value)),
            ("Destroy", 2, [{ Parameter.Ordinal: 0 } entityId, { Parameter.Ordinal: 1 } generation]) =>
                new GaDestroy(LowerValue(entityId.Value), LowerValue(generation.Value)),
            _ => throw Unsupported(invocation,
                "Call context operations directly on the in SystemContext parameter, with arguments in declaration order.")
        };
    }

    private GaStatement LowerTypedOutput(IInvocationOperation invocation, bool isContribution)
    {
        var method = invocation.TargetMethod;
        if (method.TypeArguments[0] is not INamedTypeSymbol
            { IsUnmanagedType: true, IsRefLikeType: false, IsTupleType: false } type ||
            method.Parameters.Length != 1 || !IsComponentParameter(method.Parameters[0], type) ||
            invocation.Arguments is not [{ Parameter.Ordinal: 0 } argument])
            throw Unsupported(invocation, $"Call {method.Name}<T> directly on the in SystemContext parameter with one portable payload.");

        if (!isContribution && type.SpecialType == SpecialType.System_Int32)
            return new GaEmit(LowerValue(argument.Value));

        bool scalar = IsScalarResource(type);
        if (!scalar && type is not { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None })
            throw Unsupported(invocation, $"Use a portable scalar or a closed unmanaged payload struct in {method.Name}<T>.");

        string id = QualifiedName(type);
        if (!_eventTypeIndices.TryGetValue((id, isContribution), out int eventTypeIndex))
        {
            eventTypeIndex = _eventTypes.Count;
            if (eventTypeIndex == GaOutputRecord.MaximumEventTypes)
                throw Unsupported(invocation, "The system references too many distinct typed outputs.");

            var fields = new List<GaAdditionField>();
            if (scalar)
            {
                fields.Add(new GaAdditionField("", Scalar(type, invocation),
                    type.TypeKind == TypeKind.Enum ? id : null));
            }
            else
            {
                CollectPayloadFields(type, "", fields, invocation);
            }

            var schema = new GaEventType(id, fields.ToArray(), isContribution);
            if (schema.WordCount > GaOutputRecord.MaximumEventPayloadWords)
                throw Unsupported(invocation, "The typed output exceeds the supported payload size.");
            _eventTypeIndices.Add((id, isContribution), eventTypeIndex);
            _eventTypes.Add(schema);
        }

        return new GaEmitTyped(eventTypeIndex, scalar
            ? [new GaAdditionValue(0, LowerValue(argument.Value))]
            : LowerPayload(argument.Value, _eventTypes[eventTypeIndex].Fields));
    }

    private GaStatement LowerAdd(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.TypeArguments[0] is not INamedTypeSymbol
            { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None, IsUnmanagedType: true,
                IsRefLikeType: false, IsTupleType: false } component)
            throw Unsupported(invocation, "Supply a closed unmanaged component struct to Add<T>.");

        string id = QualifiedName(component);
        if (!_additionComponentIndices.TryGetValue(id, out int componentIndex))
        {
            componentIndex = _additionComponents.Count;
            if (componentIndex == GaOutputRecord.MaximumComponentRemovals)
                throw Unsupported(invocation, "The system references too many distinct addition components.");
            var fields = new List<GaAdditionField>();
            CollectPayloadFields(component, "", fields, invocation);
            var schema = new GaAdditionComponent(id, fields.ToArray());
            if (schema.WordCount > GaOutputRecord.MaximumAdditionPayloadWords)
                throw Unsupported(invocation, "The component addition exceeds the supported payload size.");
            _additionComponentIndices.Add(id, componentIndex);
            _additionComponents.Add(schema);
        }

        if (method.Parameters.Length == 2 && IsTicketParameter(method.Parameters[0]) &&
            IsComponentParameter(method.Parameters[1], component) &&
            invocation.Arguments is [{ Parameter.Ordinal: 0 } ticket, { Parameter.Ordinal: 1 } ticketValue])
            return new GaAddTicket(componentIndex, TicketSlot(ticket, invocation),
                LowerPayload(ticketValue.Value, _additionComponents[componentIndex].Fields));
        if (method.Parameters.Length == 3 && IsInt32Parameter(method.Parameters[0]) && IsInt32Parameter(method.Parameters[1]) &&
            IsComponentParameter(method.Parameters[2], component) &&
            invocation.Arguments is [{ Parameter.Ordinal: 0 } entityId, { Parameter.Ordinal: 1 } generation, { Parameter.Ordinal: 2 } value])
            return new GaAdd(componentIndex, LowerValue(entityId.Value), LowerValue(generation.Value),
                LowerPayload(value.Value, _additionComponents[componentIndex].Fields));
        throw Unsupported(invocation, "Call Add<T> directly on the in SystemContext parameter, with arguments in declaration order.");
    }

    private GaAdditionValue[] LowerPayload(IOperation value, IReadOnlyList<GaAdditionField> fields)
    {
        value = UnwrapIdentity(value);
        if (value is IParameterReferenceOperation parameter && _components.TryGetValue(parameter.Parameter, out var source))
        {
            return fields.Select((field, index) => new GaAdditionValue(index,
                new GaLoad(FieldSlot(source, field.Path, field.Type, field.ClrEnumType), Source(value)))).ToArray();
        }

        var fieldIndices = fields.Select((field, index) => (field.Path, index))
            .ToDictionary(item => item.Path, item => item.index, StringComparer.Ordinal);
        var values = new List<GaAdditionValue>();
        LowerInitializer(value, "", fieldIndices, values);
        return values.ToArray();
    }

    private static void CollectPayloadFields(INamedTypeSymbol component, string prefix, List<GaAdditionField> fields, IOperation site)
    {
        if (component.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() switch
        {
            "System.Runtime.CompilerServices.InlineArrayAttribute" => true,
            "System.Runtime.InteropServices.StructLayoutAttribute" => attribute.ConstructorArguments[0].Value is int layout && layout != 0,
            _ => false
        }))
            throw Unsupported(site, "Use plain sequential fields in GA output payloads.");

        foreach (var field in component.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
            .OrderBy(field => field.MetadataName, StringComparer.Ordinal))
        {
            if (field.IsReadOnly || field.IsImplicitlyDeclared || field.IsFixedSizeBuffer || field.RefKind != RefKind.None)
                throw Unsupported(site, "Use writable, explicit instance fields in GA output payloads.");
            string path = prefix + field.MetadataName;
            if (field.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None,
                IsUnmanagedType: true, IsRefLikeType: false, IsTupleType: false } nested && !IsFixed(nested))
            {
                CollectPayloadFields(nested, path + ".", fields, site);
                continue;
            }
            fields.Add(new GaAdditionField(path, Scalar(field.Type, site),
                field.Type.TypeKind == TypeKind.Enum ? QualifiedName(field.Type) : null));
        }
    }

    private void LowerInitializer(IOperation value, string prefix, IReadOnlyDictionary<string, int> fields,
        List<GaAdditionValue> values)
    {
        value = UnwrapIdentity(value);
        if (value is IDefaultValueOperation)
            return;
        if (value is not IObjectCreationOperation { Arguments.Length: 0 } creation ||
            creation.Constructor is { IsImplicitlyDeclared: false })
            throw Unsupported(value, "Use default, a plain field initializer, or an entry component as the GA output payload.");
        if (creation.Initializer is null)
            return;

        foreach (var operation in creation.Initializer.Initializers)
        {
            if (operation is not ISimpleAssignmentOperation
                { IsRef: false, Target: IFieldReferenceOperation { Field.IsStatic: false } target } initializer)
                throw Unsupported(operation, "Initialize only instance fields in a GA output payload.");
            string path = prefix + target.Field.MetadataName;
            if (fields.TryGetValue(path, out int fieldIndex))
                values.Add(new GaAdditionValue(fieldIndex, LowerValue(initializer.Value)));
            else
                LowerInitializer(initializer.Value, path + ".", fields, values);
        }
    }

    private static IOperation UnwrapIdentity(IOperation operation)
    {
        while (true)
        {
            if (operation is IConversionOperation { Conversion.IsIdentity: true } conversion)
                operation = conversion.Operand;
            else if (operation is IParenthesizedOperation parentheses)
                operation = parentheses.Operand;
            else
                return operation;
        }
    }

    private GaStatement LowerRemove(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.TypeArguments[0] is not INamedTypeSymbol
            { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None, IsUnmanagedType: true,
                IsRefLikeType: false, IsTupleType: false } component)
            throw Unsupported(invocation, "Supply a closed unmanaged component struct to Remove<T>.");

        string id = QualifiedName(component);
        if (!_removalComponentIndices.TryGetValue(id, out int componentIndex))
        {
            componentIndex = _removalComponents.Count;
            if (componentIndex == GaOutputRecord.MaximumComponentRemovals)
                throw Unsupported(invocation, "The system references too many distinct removal components.");
            _removalComponentIndices.Add(id, componentIndex);
            _removalComponents.Add(id);
        }

        if (HasTicketParameter(method))
            return new GaRemoveTicket(componentIndex, TicketSlot(invocation));
        if (method.Parameters.Length == 2 && HasInt32Parameters(method) &&
            invocation.Arguments is [{ Parameter.Ordinal: 0 } entityId, { Parameter.Ordinal: 1 } generation])
            return new GaRemove(componentIndex, LowerValue(entityId.Value), LowerValue(generation.Value));
        throw Unsupported(invocation, "Call Remove<T> directly on the in SystemContext parameter, with arguments in declaration order.");
    }

    private int TicketSlot(IInvocationOperation invocation)
    {
        if (invocation.Arguments is [{ Parameter.Ordinal: 0 } argument])
            return TicketSlot(argument, invocation);
        throw Unsupported(invocation, "Pass a ticket local initialized by this invocation's Create().");
    }

    private int TicketSlot(IArgumentOperation argument, IOperation location) =>
        argument.Value is ILocalReferenceOperation reference && _ticketLocals.TryGetValue(reference.Local, out int slot)
            ? slot
            : throw Unsupported(location, "Pass a ticket local initialized by this invocation's Create().");

    private bool IsCreateCall(IInvocationOperation invocation) =>
        invocation.TargetMethod is { Name: "Create", IsStatic: false, Arity: 0, Parameters.Length: 0 } method &&
        IsSystemContext(method.ContainingType) && IsEntityTicket(method.ReturnType) && IsContextReceiver(invocation);

    private GaStatement LowerFor(IForLoopOperation loop)
    {
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

        int direction = step.Kind == OperationKind.Increment ? 1 : -1;
        int iterations = ProveLoopBound(loop, init.Symbol, start, end, comparison, step, direction);
        _insideLoop = true;
        return LowerProvenLoop(loop, LowerStatement(loop.Before[0]), comparison, loop.AtLoopBottom[0], iterations);
    }

    private GaStatement LowerWhile(IWhileLoopOperation loop, IOperation? previous)
    {
        if (_insideLoop || !loop.ConditionIsTop || loop.ConditionIsUntil
            || previous is not IVariableDeclarationGroupOperation { Declarations.Length: 1 } group
            || group.Declarations[0].Declarators is not
                [{ Symbol.RefKind: RefKind.None, Initializer.Value.ConstantValue.Value: int start } init]
            || init.Symbol.Type.SpecialType != SpecialType.System_Int32
            || loop.Condition is not IBinaryOperation { LeftOperand: ILocalReferenceOperation conditionLocal,
                RightOperand.ConstantValue.Value: int end } comparison
            || !SymbolEqualityComparer.Default.Equals(init.Symbol, conditionLocal.Local))
            throw Unsupported(loop, "Use a top-tested while preceded by an int declaration with a constant initializer and a constant comparison.");

        var operations = loop.Body is IBlockOperation block ? block.Operations : [loop.Body];
        var step = operations.Length == 0 ? null : UnwrapStep(operations[0]);
        int direction = UnitDirection(step);
        bool leadingStep = direction != 0;
        if (!leadingStep && operations.Length > 1)
        {
            step = UnwrapStep(operations[^1]);
            direction = UnitDirection(step);
        }
        if (step is null || direction == 0)
            throw Unsupported(loop, "Put an unconditional unit step at the beginning or end of the while body.");
        if (!leadingStep && loop.Body.DescendantsAndSelf().OfType<IBranchOperation>().Any(branch =>
            branch.BranchKind == BranchKind.Continue && SymbolEqualityComparer.Default.Equals(branch.Target, loop.ContinueLabel)))
            throw Unsupported(loop, "A while loop with continue requires its unit step at the beginning of the body.");

        int iterations = ProveLoopBound(loop, init.Symbol, start, end, comparison, step, direction);
        _insideLoop = true;
        return LowerProvenLoop(loop, null, comparison, null, iterations);

        static IOperation? UnwrapStep(IOperation operation) => (operation as IExpressionStatementOperation)?.Operation;

        int UnitDirection(IOperation? operation) => operation switch
        {
            IIncrementOrDecrementOperation { Target: ILocalReferenceOperation target }
                when SymbolEqualityComparer.Default.Equals(target.Local, init.Symbol) =>
                    operation.Kind == OperationKind.Increment ? 1 : -1,
            ICompoundAssignmentOperation { Target: ILocalReferenceOperation target, Value.ConstantValue.Value: 1 } compound
                when SymbolEqualityComparer.Default.Equals(target.Local, init.Symbol) => compound.OperatorKind switch
                {
                    BinaryOperatorKind.Add => 1,
                    BinaryOperatorKind.Subtract => -1,
                    _ => 0
                },
            _ => 0
        };
    }

    private int ProveLoopBound(ILoopOperation loop, ILocalSymbol counter, int start, int end,
        IBinaryOperation comparison, IOperation step, int direction)
    {
        const int maximumTripCount = 65536;
        long iterations = (direction, comparison.OperatorKind) switch
        {
            (1, BinaryOperatorKind.LessThan) => Math.Max(0, (long)end - start),
            (1, BinaryOperatorKind.LessThanOrEqual) => Math.Max(0, (long)end - start + 1),
            (-1, BinaryOperatorKind.GreaterThan) => Math.Max(0, (long)start - end),
            (-1, BinaryOperatorKind.GreaterThanOrEqual) => Math.Max(0, (long)start - end + 1),
            _ => throw Unsupported(loop, "The loop condition must match the direction of its unit step.")
        };
        long finalCounter = start + direction * iterations;
        if (finalCounter < int.MinValue || finalCounter > int.MaxValue || iterations > maximumTripCount
            || loop.Body.DescendantsAndSelf().Any(op => !ReferenceEquals(op, step) && op switch
            {
                IAssignmentOperation { Target: ILocalReferenceOperation target } =>
                    SymbolEqualityComparer.Default.Equals(target.Local, counter),
                IIncrementOrDecrementOperation { Target: ILocalReferenceOperation target } =>
                    SymbolEqualityComparer.Default.Equals(target.Local, counter),
                _ => false
            }))
            throw Unsupported(loop, "Cannot prove the loop's iteration range and induction-variable ownership.");

        return (int)iterations;
    }

    private GaStatement LowerProvenLoop(ILoopOperation loop, GaStatement? before, IBinaryOperation comparison,
        IOperation? incrementOperation, int iterations)
    {
        var condition = LowerValue(comparison);
        bool hasBranches = loop.Body.DescendantsAndSelf().OfType<IBranchOperation>().Any(branch =>
            branch.BranchKind == BranchKind.Break && SymbolEqualityComparer.Default.Equals(branch.Target, loop.ExitLabel) ||
            branch.BranchKind == BranchKind.Continue && SymbolEqualityComparer.Default.Equals(branch.Target, loop.ContinueLabel));
        GaStatement body;
        GaStatement increment;
        if (hasBranches)
        {
            _hasLoopBranches = true;
            _loopControl = new LoopControl(loop.ExitLabel, loop.ContinueLabel, AddLocal(GaScalar.UInt32));
            var source = Source(comparison);
            var notBreaking = TestLoopState(LoopBreak, GaBinaryOperator.NotEqual, source);
            var initializeState = SetLoopState(LoopRunning, source);
            before = before == null ? initializeState : new GaBlock([before, initializeState]);
            condition = new GaBinary(GaScalar.Bool, GaBinaryOperator.AndAlso, notBreaking, condition, false, source);
            body = new GaBlock([SetLoopState(LoopRunning, source), LowerStatement(loop.Body)]);
            increment = incrementOperation == null
                ? new GaBlock([])
                : new GaIf(notBreaking, LowerStatement(incrementOperation), null);
            _loopControl = null;
        }
        else
        {
            body = LowerStatement(loop.Body);
            increment = incrementOperation == null ? new GaBlock([]) : LowerStatement(incrementOperation);
        }
        var repeated = new GaLoop(condition, body, increment, iterations);
        _insideLoop = false;
        return before == null ? repeated : new GaBlock([before, repeated]);
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
            var body = LowerBlock(arm.Body, match.ExitLabel);
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

    private GaSlot AddLocal(GaScalar type)
    {
        var local = new GaSlot(false, _localTypes.Count, type);
        _localTypes.Add(type);
        return local;
    }

    private GaValue LowerValue(IOperation operation)
    {
        var type = Scalar(ResolveHelperType(operation.Type), operation);
        var source = Source(operation);
        if (operation.ConstantValue is { HasValue: true } constant)
            return new GaConstant(type, constant.Value switch
            {
                bool enabled => enabled ? 1u : 0u,
                int value => unchecked((uint)value),
                uint value => value,
                _ => throw Unsupported(operation, "Only canonical 32-bit scalar constants are supported.")
            }, source);

        if (FindInlineAccess(operation) is { } access)
            return LowerArrayAccess(operation, access, writable: false);
        if (IsResourcePath(operation))
            return LowerResourceRead(operation, type, source);
        if (FindBufferRead(operation) is { } buffer)
            return LowerBufferRead(operation, buffer, type);

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
            IParameterReferenceOperation parameter when _helper != null =>
                new GaLoad(_helper.Parameters[parameter.Parameter.Ordinal], source),
            ILocalReferenceOperation local when _helper != null && _helper.Locals.TryGetValue(local.Local, out var slot) =>
                new GaLoad(slot, source),
            IFieldReferenceOperation field when FindCapturedField(field) is { } captured => new GaLoad(captured, source),
            ILocalReferenceOperation or IFieldReferenceOperation => new GaLoad(Slot(operation), source),
            IParenthesizedOperation parentheses => LowerValue(parentheses.Operand),
            IConversionOperation conversion when conversion.OperatorMethod != null &&
                IsFixed(conversion.OperatorMethod.ContainingType) => ConvertFixed(type, LowerValue(conversion.Operand), conversion),
            IConversionOperation { OperatorMethod: { } method } conversion =>
                LowerValueMember(conversion, method, null, [conversion.Operand]),
            IConversionOperation conversion when !conversion.Conversion.IsUserDefined &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsNumeric ||
                 conversion.Operand.Type?.TypeKind == TypeKind.Enum || conversion.Type?.TypeKind == TypeKind.Enum)
                => ConvertValue(type, conversion),
            IUnaryOperation { IsLifted: false } unary when IsBuiltinOrFixed(unary.OperatorMethod) => LowerUnary(type, unary),
            IBinaryOperation { IsLifted: false } binary when IsBuiltinOrFixed(binary.OperatorMethod) => Binary(type, binary.OperatorKind,
                LowerValue(binary.LeftOperand), LowerValue(binary.RightOperand), binary.IsChecked, binary),
            IUnaryOperation { IsLifted: false, OperatorMethod: { } method } unary =>
                LowerValueMember(unary, method, null, [unary.Operand]),
            IBinaryOperation { IsLifted: false, OperatorMethod: { } method,
                OperatorKind: not (BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr) } binary =>
                LowerValueMember(binary, method, null, [binary.LeftOperand, binary.RightOperand]),
            IPropertyReferenceOperation { Property: { IsIndexer: false, GetMethod: { } getter }, Arguments.Length: 0 } property =>
                LowerValueMember(property, getter, property.Instance, []),
            IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType) &&
                invocation.TargetMethod.Name is ("Has" or "TryRead") => LowerOptionalAccess(invocation),
            IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType) &&
                invocation.TargetMethod.Name == "BufferLength" => LowerBufferLength(invocation),
            IInvocationOperation invocation when IsSystemContext(invocation.TargetMethod.ContainingType) =>
                LowerSnapshotRead(invocation),
            IInvocationOperation invocation when IsFixed(invocation.TargetMethod.ContainingType) => LowerFixedCall(invocation),
            IInvocationOperation invocation => LowerHelper(invocation),
            IConditionalOperation conditional when conditional.WhenFalse != null && !conditional.IsRef =>
                new GaSelect(type, LowerValue(conditional.Condition), LowerValue(conditional.WhenTrue),
                    LowerValue(conditional.WhenFalse), source),
            IDefaultValueOperation => new GaConstant(type, 0, source),
            _ => throw Unsupported(operation)
        };
    }

    private GaValue LowerHelper(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod;
        var definition = target.OriginalDefinition.PartialImplementationPart ?? target.OriginalDefinition;
        if (target is not { IsStatic: true, MethodKind: MethodKind.Ordinary, ReturnsVoid: false,
                ReturnsByRef: false, ReturnsByRefReadonly: false, ContainingType.IsGenericType: false } ||
            definition.DeclaringSyntaxReferences is not [var reference] ||
            reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration ||
            target.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams) ||
            invocation.Arguments.Length != target.Parameters.Length ||
            invocation.Arguments.Any(argument => argument.ArgumentKind != ArgumentKind.Explicit))
            throw Unsupported(invocation, "Use a source-defined static scalar helper with explicit by-value arguments.");
        if (!target.ContainingType.StaticConstructors.IsEmpty)
            throw Unsupported(invocation, "A GA helper cannot depend on static initialization of its declaring type.");

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        if (model.GetOperation(declaration, cancellationToken) is not IMethodBodyOperation body ||
            (body.ExpressionBody ?? body.BlockBody) is not { Operations.Length: > 0 } block ||
            block.Operations[^1] is not IReturnOperation { ReturnedValue: { } result })
            throw Unsupported(invocation, "Use an expression body or a scalar return preceded by initialized locals.");

        var source = Source(invocation);
        var parameters = new GaSlot[target.Parameters.Length];
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        // Named arguments still evaluate in source order. Bind each before entering the helper.
        foreach (var argument in invocation.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
        {
            var value = LowerValue(argument.Value);
            int ordinal = argument.Parameter!.Ordinal;
            var slot = AddLocal(Scalar(ResolveHelperType(target.Parameters[ordinal].Type), argument));
            parameters[ordinal] = slot;
            bindings.Add((slot, value, Source(argument)));
        }

        var types = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        for (int i = 0; i < definition.TypeParameters.Length; i++)
        {
            var type = ResolveHelperType(target.TypeArguments[i])!;
            _ = Scalar(type, invocation);
            types.Add(definition.TypeParameters[i], type);
        }

        var frame = new HelperFrame(parameters, new Dictionary<ILocalSymbol, GaSlot>(SymbolEqualityComparer.Default), types);
        return LowerHelperBody(invocation, definition, declaration, block.Operations.RemoveAt(block.Operations.Length - 1),
            result, frame, bindings);
    }

    private GaValue LowerValueMember(IOperation site, IMethodSymbol target, IOperation? receiver, IOperation[] arguments)
    {
        var definition = target.OriginalDefinition;
        if (target.ReturnsVoid || target.ReturnsByRef || target.ReturnsByRefReadonly ||
            target.ContainingType.IsGenericType || target.IsVirtual || target.IsAbstract ||
            !target.ContainingType.StaticConstructors.IsEmpty ||
            !target.IsStatic && (receiver == null || target.ContainingType.TypeKind != TypeKind.Struct ||
                !(target.IsReadOnly || target.ContainingType.IsReadOnly)) ||
            arguments.Length != target.Parameters.Length || target.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
            definition.DeclaringSyntaxReferences is not [var reference])
            throw Unsupported(site, "Use a source-defined scalar operator, conversion, or static or readonly value getter.");

        var declaration = reference.GetSyntax(cancellationToken);
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var block = model.GetOperation(declaration, cancellationToken) switch
        {
            IMethodBodyOperation body => body.ExpressionBody ?? body.BlockBody,
            IBlockOperation body => body,
            _ => null
        };
        var (arrow, bodySyntax) = declaration switch
        {
            OperatorDeclarationSyntax syntax => (syntax.ExpressionBody, syntax.Body),
            ConversionOperatorDeclarationSyntax syntax => (syntax.ExpressionBody, syntax.Body),
            AccessorDeclarationSyntax syntax => (syntax.ExpressionBody, syntax.Body),
            PropertyDeclarationSyntax syntax => (syntax.ExpressionBody, (BlockSyntax?)null),
            ArrowExpressionClauseSyntax syntax => (syntax, (BlockSyntax?)null),
            _ => ((ArrowExpressionClauseSyntax?)null, (BlockSyntax?)null)
        };
        if (block == null && bodySyntax != null)
            block = model.GetOperation(bodySyntax, cancellationToken) as IBlockOperation;
        if (block == null && arrow != null)
            block = model.GetOperation(arrow, cancellationToken) as IBlockOperation;
        while (block?.Operations is [IBlockOperation nested])
            block = nested;

        IReadOnlyList<IOperation> locals = [];
        IOperation? result;
        if (block?.Operations is { Length: > 0 } operations &&
            operations[^1] is IReturnOperation { ReturnedValue: { } returned })
        {
            locals = operations.RemoveAt(operations.Length - 1);
            result = returned;
        }
        else
        {
            result = arrow == null ? null : model.GetOperation(arrow.Expression, cancellationToken);
            while (result?.Parent is IConversionOperation { IsImplicit: true } conversion)
                result = conversion;
        }
        if (result == null || Scalar(result.Type, result) != Scalar(target.ReturnType, site))
            throw Unsupported(site, "Use an expression body or a scalar return preceded by initialized locals.");

        var frame = new HelperFrame(new GaSlot[arguments.Length],
            new Dictionary<ILocalSymbol, GaSlot>(SymbolEqualityComparer.Default),
            new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default));
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        if (receiver != null)
            BindMemberInput(frame, ReceiverInput, target.ContainingType, receiver, bindings);
        for (int i = 0; i < arguments.Length; i++)
            BindMemberInput(frame, i, target.Parameters[i].Type, arguments[i], bindings);
        return LowerHelperBody(site, definition, declaration, locals, result, frame, bindings);
    }

    private void BindMemberInput(HelperFrame frame, int index, ITypeSymbol type, IOperation operation,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (IsAggregate(type))
        {
            frame.AggregateInputs.Add(index, CaptureAggregate(type, operation, bindings));
            return;
        }

        var value = LowerValue(operation);
        var local = AddLocal(Scalar(type, operation));
        frame.Parameters[index] = local;
        bindings.Add((local, value, Source(operation)));
    }

    private Dictionary<string, GaSlot> CaptureAggregate(ITypeSymbol type, IOperation operation,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        var fields = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
        var source = Source(operation);
        var path = new Stack<string>();
        bool readOnly = false;
        var root = ReadArrayFieldPath(UnwrapIdentity(operation), path, ref readOnly);
        string prefix = path.Count == 0 ? "" : string.Join(".", path) + ".";
        var capturedFields = FindCapturedFields(root);
        var component = capturedFields == null ? ResolveStorage(root, operation) : null;
        BindFields((INamedTypeSymbol)type, "", readOnly);
        return fields;

        void BindFields(INamedTypeSymbol aggregate, string fieldPrefix, bool parentReadOnly)
        {
            if (GetInlineArrayDeclaration(aggregate) != null || aggregate.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
                attribute.ConstructorArguments is [{ Value: int layout }] && layout != 0))
                throw Unsupported(operation, "Capture only sequential structs with explicit scalar fields.");
            foreach (var field in aggregate.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
                .OrderBy(field => field.MetadataName, StringComparer.Ordinal))
            {
                if (field.IsImplicitlyDeclared || field.IsFixedSizeBuffer || field.RefKind != RefKind.None ||
                    field.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                    throw Unsupported(operation, "Capture only accessible explicit instance fields.");
                string fieldPath = fieldPrefix + field.MetadataName;
                bool fieldReadOnly = parentReadOnly || field.IsReadOnly;
                if (IsAggregate(field.Type))
                {
                    BindFields((INamedTypeSymbol)field.Type, fieldPath + ".", fieldReadOnly);
                    continue;
                }
                var scalar = Scalar(field.Type, operation);
                string? enumType = field.Type.TypeKind == TypeKind.Enum ? QualifiedName(field.Type) : null;
                GaSlot captured;
                if (component != null)
                    captured = FieldSlot(component, prefix + fieldPath, scalar, enumType,
                        fieldReadOnly ? GaFieldAccess.Read : component.Access);
                else if (!capturedFields!.TryGetValue(prefix + fieldPath, out captured))
                    throw Unsupported(operation, "Capture a field path from a bound value operand or getter receiver.");
                var local = AddLocal(scalar);
                fields.Add(fieldPath, local);
                bindings.Add((local, new GaLoad(captured, source), source));
            }
        }
    }

    private static bool IsAggregate(ITypeSymbol type) => type is INamedTypeSymbol
        { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None, IsUnmanagedType: true,
            IsRefLikeType: false, IsTupleType: false } && !IsFixed(type);

    private Dictionary<string, GaSlot>? FindCapturedFields(IOperation? root)
    {
        if (root is ILocalReferenceOperation local)
            return _aggregateLocals.GetValueOrDefault(local.Local);
        return root switch
        {
            IParameterReferenceOperation parameter => _helper?.AggregateInputs.GetValueOrDefault(parameter.Parameter.Ordinal),
            IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } =>
                _helper?.AggregateInputs.GetValueOrDefault(ReceiverInput),
            _ => null
        };
    }

    private GaSlot? FindCapturedField(IFieldReferenceOperation field)
    {
        if ((_helper == null && _aggregateLocals.Count == 0) || field.Field.IsStatic)
            return null;
        var path = new Stack<string>();
        bool readOnly = false;
        var root = ReadArrayFieldPath(field, path, ref readOnly);
        return FindCapturedFields(root) is { } fields && fields.TryGetValue(string.Join(".", path), out var slot) ? slot : null;
    }

    private GaValue LowerHelperBody(IOperation site, IMethodSymbol definition, SyntaxNode declaration,
        IReadOnlyList<IOperation> operations, IOperation result, HelperFrame frame,
        List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        if (!_activeHelpers.Add(definition))
            throw Unsupported(site, "Recursive GA helpers and value members are not supported.");
        var previous = _helper;
        _helper = frame;
        try
        {
            foreach (var operation in operations)
            {
                if (operation is not IVariableDeclarationGroupOperation group)
                    throw Unsupported(operation, "Use initialized scalar locals before the helper return.");
                foreach (var declarator in group.Declarations.SelectMany(item => item.Declarators))
                {
                    if (declarator.Symbol.RefKind != RefKind.None || declarator.Initializer?.Value is not { } initializer)
                        throw Unsupported(declarator, "Initialize the helper local by value.");
                    var value = LowerValue(initializer);
                    var slot = AddLocal(Scalar(ResolveHelperType(declarator.Symbol.Type), declarator));
                    frame.Locals.Add(declarator.Symbol, slot);
                    bindings.Add((slot, value, Source(declarator)));
                }
            }

            GaValue lowered = LowerValue(result);
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                var binding = bindings[i];
                lowered = new GaBoundValue(binding.Slot, binding.Value, lowered, binding.Source);
            }
            if (!SymbolEqualityComparer.Default.Equals(definition.ContainingType, entry.ContainingType))
                _externalHelperSources.Add(definition.GetDocumentationCommentId() + "\n" + declaration.ToFullString().ReplaceLineEndings("\n"));
            return lowered;
        }
        finally
        {
            _helper = previous;
            _activeHelpers.Remove(definition);
        }
    }

    private bool IsResourcePath(IOperation operation)
    {
        var root = Unwrap(operation);
        while (root is IFieldReferenceOperation field)
            root = Unwrap(field.Instance);
        return root is IInvocationOperation invocation && IsResourceCall(invocation) ||
            root is ILocalReferenceOperation local && _resourceAliases.Contains(local.Local);
    }

    private bool IsResourceCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.Name == "Resource" && method.ReturnsByRefReadonly && IsSystemContext(method.ContainingType) &&
            ValidContextCall(invocation, argumentCount: 0) &&
            SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], _resourceType) &&
            SymbolEqualityComparer.Default.Equals(method.ReturnType, _resourceType);
    }

    private GaResourceRead LowerResourceRead(IOperation operation, GaScalar type, GaSourceSpan source)
    {
        var path = new Stack<string>();
        bool readOnly = true;
        ReadArrayFieldPath(operation, path, ref readOnly);
        string name = string.Join(".", path);
        if (!_resourceFieldIndices.TryGetValue(name, out int index))
        {
            index = _resourceFields.Count;
            string? enumType = operation.Type is { TypeKind: TypeKind.Enum } symbol ? QualifiedName(symbol) : null;
            _resourceFields.Add(new GaTickResourceField(name, type, enumType));
            _resourceFieldIndices.Add(name, index);
        }
        return new GaResourceRead(index, type, source);
    }

    private IInvocationOperation? FindBufferRead(IOperation operation)
    {
        var root = Unwrap(operation);
        while (root is IFieldReferenceOperation field)
            root = Unwrap(field.Instance);
        return root is IInvocationOperation invocation && IsBufferCall(invocation, argumentCount: 1) &&
            invocation.TargetMethod is { Name: "ReadBuffer", ReturnsByRefReadonly: true } method &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 &&
            method.Parameters[0].RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(method.ReturnType, _bufferType) ? invocation : null;
    }

    private bool IsBufferCall(IInvocationOperation invocation, int argumentCount) =>
        _bufferType != null && IsSystemContext(invocation.TargetMethod.ContainingType) &&
        ValidContextCall(invocation, argumentCount) &&
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.TypeArguments[0], _bufferType);

    private GaBufferRead LowerBufferRead(IOperation operation, IInvocationOperation invocation, GaScalar type)
    {
        var path = new Stack<string>();
        bool readOnly = true;
        ReadArrayFieldPath(operation, path, ref readOnly);
        string name = string.Join(".", path);
        if (!_bufferFieldIndices.TryGetValue(name, out int index))
        {
            index = _bufferFields.Count;
            string? enumType = operation.Type is { TypeKind: TypeKind.Enum } symbol ? QualifiedName(symbol) : null;
            _bufferFields.Add(new GaBufferField(name, type, enumType));
            _bufferFieldIndices.Add(name, index);
        }
        return new GaBufferRead(index, LowerValue(invocation.Arguments[0].Value), type, Source(invocation));
    }

    private GaBufferCount LowerBufferLength(IInvocationOperation invocation)
    {
        if (!IsBufferCall(invocation, argumentCount: 0) ||
            invocation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Int32 ||
            invocation.TargetMethod.ReturnsByRef || invocation.TargetMethod.ReturnsByRefReadonly)
            throw Unsupported(invocation, "Call BufferLength<T>() directly on the entry context with the declared read buffer element type.");
        return new GaBufferCount(Source(invocation));
    }

    private GaValue LowerOptionalAccess(IInvocationOperation invocation)
    {
        bool copy = invocation.TargetMethod.Name == "TryRead";
        if (!ValidContextCall(invocation, argumentCount: copy ? 1 : 0) ||
            invocation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean ||
            !_declaredAccesses.TryGetValue(invocation.TargetMethod.TypeArguments[0], out var binding) ||
            binding.Component.Origin != GaComponentOrigin.Optional)
            throw Unsupported(invocation, "Call Has<T> or TryRead<T> directly on the entry context for a declared OptionalRead component.");

        var source = Source(invocation);
        if (!copy)
            return new GaHasOwn(binding.Index, source);
        if (invocation.Arguments[0].Parameter?.RefKind != RefKind.Out)
            throw Unsupported(invocation, "Pass a value local or discard as the out argument to TryRead<T>.");
        return OptionalOutTarget(invocation) switch
        {
            IDiscardOperation => new GaTryRead(binding.Index, [], source),
            ILocalReferenceOperation { Local.RefKind: RefKind.None } local
                when _optionalLocals.TryGetValue(local.Local, out var optional) =>
                    new GaTryRead(binding.Index, optional.Copies.AsReadOnly(), source),
            _ => throw Unsupported(invocation, "Pass a value local or discard as the out argument to TryRead<T>.")
        };
    }

    private static IOperation? OptionalOutTarget(IInvocationOperation invocation)
    {
        var value = Unwrap(invocation.Arguments[0].Value);
        return value is IDeclarationExpressionOperation declaration ? declaration.Expression : value;
    }

    private GaComponent ResolveOwn(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.Name != "Own" || !IsSystemContext(method.ContainingType) ||
            !ValidContextCall(invocation, argumentCount: 0) || !method.ReturnsByRef ||
            !_declaredAccesses.TryGetValue(method.TypeArguments[0], out var binding) ||
            binding.Component.Origin != GaComponentOrigin.Own)
            throw Unsupported(invocation, "Call Own<T> directly on the entry context for a declared required own component.");
        return binding.Component;
    }

    private bool ValidContextCall(IInvocationOperation invocation, int argumentCount) =>
        invocation.TargetMethod is { IsStatic: false, Arity: 1 } method &&
        method.Parameters.Length == argumentCount && invocation.Arguments.Length == argumentCount &&
        IsContextReceiver(invocation);

    private static bool IsOwnComponent(INamedTypeSymbol type) =>
        type is { IsUnmanagedType: true, TypeKind: TypeKind.Struct, SpecialType: SpecialType.None,
            IsRefLikeType: false, IsTupleType: false, IsFileLocal: false, IsUnboundGenericType: false } &&
        !HasExplicitLayout(type);

    private GaSnapshotRead LowerSnapshotRead(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.Name == "ReadSnapshot")
            return LowerTypedSnapshotRead(invocation);
        if (method is not { Name: "ReadSnapshotInt32", IsStatic: false, Arity: 0 } ||
            method.ReturnType.SpecialType != SpecialType.System_Int32 ||
            method.Parameters.Length != 3 ||
            !HasInt32Parameters(method) ||
            invocation.Arguments is not [{ Parameter.Ordinal: 0 } entityId,
                { Parameter.Ordinal: 1 } generation, { Parameter.Ordinal: 2 } fallback] ||
            !IsContextReceiver(invocation))
            throw Unsupported(invocation, "Call ReadSnapshotInt32 directly on the in SystemContext parameter, with arguments in declaration order.");
        if (_snapshotLayout is { } layout && layout.Fields is not [GaScalar.Int32])
            throw Unsupported(invocation, "ReadSnapshotInt32 requires exactly one Int32 snapshot field.");
        return new GaSnapshotRead(LowerValue(entityId.Value), LowerValue(generation.Value),
            LowerValue(fallback.Value), Source(invocation));
    }

    private GaSnapshotRead LowerTypedSnapshotRead(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method is not { IsStatic: false, Arity: 1 } ||
            method.Parameters.Length != 4 || !method.Parameters.Take(3).All(IsInt32Parameter) ||
            !IsComponentParameter(method.Parameters[3], method.ReturnType) ||
            invocation.Arguments is not [{ Parameter.Ordinal: 0 } entityId, { Parameter.Ordinal: 1 } generation,
                { Parameter.Ordinal: 2 } field, { Parameter.Ordinal: 3 } fallback] ||
            !IsContextReceiver(invocation))
            throw Unsupported(invocation, "Call ReadSnapshot<T> directly on the in SystemContext parameter, with arguments in declaration order.");

        var type = method.ReturnType.SpecialType switch
        {
            SpecialType.System_Boolean => GaScalar.Bool,
            SpecialType.System_Int32 => GaScalar.Int32,
            SpecialType.System_UInt32 => GaScalar.UInt32,
            _ when IsFixed(method.ReturnType) => GaScalar.Fixed,
            _ => throw Unsupported(invocation, "ReadSnapshot<T> supports bool, int, uint and Fixed.")
        };
        if (field.Value.ConstantValue is not { HasValue: true, Value: int index } ||
            _snapshotLayout is not { } layout || (uint)index >= (uint)layout.Fields.Length || layout.Fields[index] != type)
            throw Unsupported(invocation, "ReadSnapshot<T> requires a constant field index matching the declared snapshot layout.");

        return new GaSnapshotRead(LowerValue(entityId.Value), LowerValue(generation.Value),
            LowerValue(fallback.Value), Source(invocation)) { FieldIndex = index };
    }

    private bool IsContextReceiver(IInvocationOperation invocation) =>
        invocation.Instance is IParameterReferenceOperation receiver &&
        SymbolEqualityComparer.Default.Equals(receiver.Parameter, _context);

    private static bool HasInt32Parameters(IMethodSymbol method) =>
        method.Parameters.All(IsInt32Parameter);

    private static bool IsInt32Parameter(IParameterSymbol parameter) =>
        parameter.RefKind == RefKind.None && parameter.Type.SpecialType == SpecialType.System_Int32;

    private static bool IsComponentParameter(IParameterSymbol parameter, ITypeSymbol type) =>
        parameter.RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(parameter.Type, type);

    private static bool IsTicketParameter(IParameterSymbol parameter) =>
        parameter.RefKind == RefKind.None && IsEntityTicket(parameter.Type);

    private static bool HasTicketParameter(IMethodSymbol method) =>
        method.Parameters.Length == 1 && IsTicketParameter(method.Parameters[0]);

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

        bool readOnly = false;
        var path = new Stack<string>();
        var receiver = ReadArrayFieldPath(leaf, path, ref readOnly);
        string pathName = string.Join(".", path);
        var type = Scalar(leaf.Type, leaf);
        string? enumType = leaf.Type is { TypeKind: TypeKind.Enum } symbol ? QualifiedName(symbol) : null;
        if (receiver is ILocalReferenceOperation copy && _optionalLocals.TryGetValue(copy.Local, out var optional))
            return OptionalFieldSlot(optional, pathName, type, enumType, leaf);

        var component = ResolveStorage(receiver, operation);
        return FieldSlot(component, pathName, type, enumType, readOnly ? GaFieldAccess.Read : component.Access);
    }

    private GaSlot OptionalFieldSlot(OptionalCopy copy, string path, GaScalar type, string? enumType, IOperation operation)
    {
        if (copy.Locals.TryGetValue(path, out var local))
            return local;
        local = AddLocal(type);
        copy.Locals.Add(path, local);
        copy.Copies.Add(new GaOptionalFieldCopy(local, FieldSlot(copy.Component, path, type, enumType)));
        var clear = new GaStore(local, new GaConstant(type, 0, Source(operation)));
        copy.Defaults.Add(clear);
        _optionalDefaults.Add(clear);
        return local;
    }

    private GaComponent ResolveStorage(IOperation? receiver, IOperation operation)
    {
        if (receiver is IParameterReferenceOperation parameter && _components.TryGetValue(parameter.Parameter, out var component))
            return component;
        if (receiver is ILocalReferenceOperation local && _ownAliases.TryGetValue(local.Local, out var own))
            return own;
        if (receiver is IInvocationOperation invocation && invocation.TargetMethod.Name == "Own")
            return ResolveOwn(invocation);
        throw Unsupported(operation, "Fields must come from a non-overlaid query component or declared own component.");
    }

    private GaSlot FieldSlot(GaComponent component, string pathName, GaScalar type, string? enumType,
        GaFieldAccess? access = null)
    {
        var key = (component.Id, pathName);
        if (_fields.TryGetValue(key, out var fieldSlot))
            return fieldSlot;
        fieldSlot = new GaSlot(true, _schema.Count, type);
        _fields.Add(key, fieldSlot);
        _schema.Add(new GaField(component.Id, pathName, type, access ?? component.Access)
        {
            ClrEnumType = enumType
        });
        return fieldSlot;
    }

    private static IInlineArrayAccessOperation? FindInlineAccess(IOperation operation)
    {
        IOperation? receiver = Unwrap(operation);
        while (receiver is IFieldReferenceOperation field)
            receiver = Unwrap(field.Instance);
        return receiver as IInlineArrayAccessOperation;
    }

    private GaArrayRead LowerArrayAccess(IOperation operation, IInlineArrayAccessOperation access, bool writable)
    {
        if (access.Argument.Type?.SpecialType != SpecialType.System_Int32 ||
            access.Instance.Type is not INamedTypeSymbol { IsUnmanagedType: true } buffer ||
            HasExplicitLayout(buffer))
            throw Unsupported(access, "Use an Int32 element index on a portable inline array.");

        var declaration = GetInlineArrayDeclaration(buffer);
        if (declaration?.ConstructorArguments is not [{ Value: int length }] || length <= 0)
            throw Unsupported(access, "The inline array must declare a constant positive length with the runtime InlineArray attribute.");

        bool readOnly = false;
        var path = new Stack<string>();
        ReadArrayFieldPath(operation, path, ref readOnly);
        string suffix = path.Count == 0 ? "" : "." + string.Join(".", path);
        path.Clear();
        var root = ReadArrayFieldPath(access.Instance, path, ref readOnly);
        if (root is IInlineArrayAccessOperation)
            throw Unsupported(access, "Use one inline array element index per component field path.");
        var component = ResolveStorage(root, access);

        var fieldAccess = readOnly ? GaFieldAccess.Read : component.Access;
        if (writable && fieldAccess != GaFieldAccess.ReadWrite)
            throw Unsupported(operation, "The target inline array field was declared read-only.");
        var type = Scalar(operation.Type, operation);
        string prefix = string.Join(".", path);
        var key = (component.Id, prefix + "[]" + suffix);
        if (!_arrayFields.TryGetValue(key, out var group))
        {
            group = new GaArrayField(_schema.Count, length, type);
            string? enumType = operation.Type is { TypeKind: TypeKind.Enum } scalarEnum ? QualifiedName(scalarEnum) : null;
            for (int index = 0; index < length; index++)
            {
                string element = index.ToString(CultureInfo.InvariantCulture);
                FieldSlot(component, $"{prefix}[{element}]{suffix}", type, enumType, fieldAccess);
            }
            _arrayFields.Add(key, group);
        }
        return new GaArrayRead(group, LowerValue(access.Argument), Source(access));
    }

    private static IOperation? ReadArrayFieldPath(IOperation operation, Stack<string> path, ref bool readOnly)
    {
        IOperation? receiver = Unwrap(operation);
        while (receiver is IFieldReferenceOperation field)
        {
            if (GetInlineArrayDeclaration(field.Field.ContainingType) != null)
                throw Unsupported(field, "Access an inline array backing field through index 0.");
            if (field.Field.IsStatic || HasExplicitLayout(field.Field.ContainingType) ||
                field.Field.IsFixedSizeBuffer || field.Field.RefKind != RefKind.None ||
                field.Field.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                throw Unsupported(field, "Component paths must use accessible fields of non-overlaid value types.");
            path.Push(field.Field.MetadataName);
            readOnly |= field.Field.IsReadOnly;
            receiver = Unwrap(field.Instance);
        }
        return receiver;
    }

    private static AttributeData? GetInlineArrayDeclaration(INamedTypeSymbol type) =>
        type.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass is { } attributeType && attributeType.DeclaringSyntaxReferences.Length == 0 &&
            attributeType.ContainingAssembly.Identity.Name is "System.Runtime" or "System.Private.CoreLib" &&
            QualifiedName(attributeType) == "global::System.Runtime.CompilerServices.InlineArrayAttribute");

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IParenthesizedOperation parentheses)
            operation = parentheses.Operand;
        return operation;
    }

    private static bool HasExplicitLayout(ITypeSymbol type) => type.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
        attribute.ConstructorArguments[0].Value is 2);

    private ITypeSymbol? ResolveHelperType(ITypeSymbol? type) =>
        type is ITypeParameterSymbol parameter && _helper != null && _helper.Types.TryGetValue(parameter, out var argument)
            ? argument : type;

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

    private static bool IsSystemContext(ITypeSymbol type) =>
        type.IsRefLikeType && type.ContainingAssembly?.Identity.Name == "SKECS.GA" &&
        type.DeclaringSyntaxReferences.Length == 0 && QualifiedName(type) == "global::SKECS.GA.SystemContext";

    private static bool IsEntityTicket(ITypeSymbol type) =>
        type.IsRefLikeType && type.ContainingAssembly?.Identity.Name == "SKECS.GA" &&
        type.DeclaringSyntaxReferences.Length == 0 && QualifiedName(type) == "global::SKECS.GA.GaEntityTicket";

    private GaSourceSpan Source(IOperation operation) => Source(operation.Syntax);

    private GaSourceSpan Source(SyntaxNode syntax)
    {
        var path = syntax.SyntaxTree.FilePath;
        return new GaSourceSpan(compilation.Options.SourceReferenceResolver?.NormalizePath(path, null) ?? path,
            syntax.SpanStart, syntax.Span.Length);
    }

    private static string QualifiedName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static GaLoweringException Unsupported(IOperation operation, string? detail = null) =>
        new(operation.Syntax.GetLocation(), detail ?? $"{operation.Kind} has no lowering in the integer profile.");
}
