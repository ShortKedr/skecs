using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA.Analysis;

public static partial class GaSemanticAnalysis
{
    private static readonly DiagnosticDescriptor Schema = Error("GA0001", "Unsupported GA value schema");
    private static readonly DiagnosticDescriptor Arithmetic = Error("GA0002", "Nonportable GA arithmetic");
    private static readonly DiagnosticDescriptor Managed = Error("GA0003", "Forbidden GA state or operation");
    private static readonly DiagnosticDescriptor Unknown = Error("GA0004", "Unverified GA operation");
    private static readonly DiagnosticDescriptor Write = Error("GA0005", "Unproven GA write ownership");
    private static readonly DiagnosticDescriptor Stream = Error("GA0006", "Unverified GA event or command");
    private static readonly DiagnosticDescriptor Fallback = new("GA0007", "GA system requires CPU execution", "{0}",
        "SKECS.GA", DiagnosticSeverity.Info, isEnabledByDefault: true);

    internal static ImmutableArray<DiagnosticDescriptor> Rules { get; } =
        ImmutableArray.Create(Schema, Arithmetic, Managed, Unknown, Write, Stream, Fallback);

    public static bool IsGaSystem(INamedTypeSymbol type, Compilation compilation)
    {
        var marker = compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute");
        return marker != null && type.GetAttributes().Any(attribute => Equal(attribute.AttributeClass, marker));
    }

    public static ImmutableArray<IMethodSymbol> GetEntryPoints(INamedTypeSymbol type, Compilation compilation)
    {
        if (!IsGaSystem(type, compilation) || type.TypeKind != TypeKind.Struct || type.IsRefLikeType ||
            type.IsRecord || type.IsGenericType || type.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic) ||
            !type.DeclaringSyntaxReferences.All(declaration => declaration.GetSyntax() is StructDeclarationSyntax syntax &&
                syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return ImmutableArray<IMethodSymbol>.Empty;

        var entries = type.GetMembers("Execute").OfType<IMethodSymbol>().ToImmutableArray();
        if (entries.Length != 1)
            return entries;

        var entry = entries[0];
        return entry.MethodKind == MethodKind.Ordinary && !entry.IsStatic && entry.Arity == 0 && entry.ReturnsVoid &&
            entry.DeclaredAccessibility == Accessibility.Public
            ? entries : ImmutableArray<IMethodSymbol>.Empty;
    }

    public static Diagnostic InvalidSystem(INamedTypeSymbol type)
    {
        if (type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => !field.IsStatic) is { } field)
            return Diagnostic.Create(Managed, Source(field),
                $"Pass '{field.Type.ToDisplayString()}' through declared value inputs instead of capturing '{field.Name}' in the GA system.", "");
        return Diagnostic.Create(Schema, Source(type),
            $"Use one public instance void Execute method on the stateless, nongeneric partial struct '{type.Name}'.", "");
    }

    public static ImmutableArray<Diagnostic> Analyze(IMethodSymbol entry, Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        var entries = GetEntryPoints(entry.ContainingType, compilation);
        if (entries.Length != 1 || !Equal(entries[0], entry))
            return ImmutableArray.Create(InvalidSystem(entry.ContainingType));
        var verifier = new Verifier(compilation, entry, cancellationToken);
        return verifier.Run();
    }

    public static Diagnostic CpuFallback(Location location, string reason) => Diagnostic.Create(Fallback, location, reason);

    private static DiagnosticDescriptor Error(string id, string title) => new(id, title, "{0}{1}", "SKECS.GA",
        DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    private static Location Source(ISymbol symbol) => symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

    internal static bool Equal(ISymbol? left, ISymbol? right) => SymbolEqualityComparer.Default.Equals(left, right);

    private sealed partial class Verifier : OperationWalker
    {
        private readonly Compilation compilation;
        private readonly IMethodSymbol entry;
        private readonly CancellationToken cancellationToken;
        private readonly INamedTypeSymbol? entity;
        private readonly INamedTypeSymbol? context;
        private readonly INamedTypeSymbol? ticket;
        private readonly INamedTypeSymbol? fixedType;
        private ImmutableArray<TypedConstant> snapshotFields;
        private readonly List<IMethodSymbol> callChain = new();
        private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> verified = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ILocalSymbol> ticketLocals = new(SymbolEqualityComparer.Default);
        private readonly HashSet<(string Id, SyntaxTree? Tree, int Start, string Reason)> reported = new();
        private readonly ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        public Verifier(Compilation compilation, IMethodSymbol entry, CancellationToken cancellationToken)
        {
            this.compilation = compilation;
            this.entry = entry;
            this.cancellationToken = cancellationToken;
            entity = compilation.GetTypeByMetadataName("SKECS.Entity");
            context = compilation.GetTypeByMetadataName("SKECS.GA.SystemContext");
            ticket = compilation.GetTypeByMetadataName("SKECS.GA.GaEntityTicket");
            fixedType = compilation.GetTypeByMetadataName("SKECS.Numerics.Fixed");
        }

        public ImmutableArray<Diagnostic> Run()
        {
            ValidateSnapshotDeclaration();
            var components = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var parameter in entry.Parameters)
            {
                var type = parameter.Type;
                if (Equal(type, context))
                {
                    if (parameter.RefKind != RefKind.In || !components.Add(type))
                        Report(Write, Source(parameter), "Declare a single in SystemContext parameter.");
                    continue;
                }
                if (Equal(type, entity))
                {
                    if (parameter.RefKind is not (RefKind.None or RefKind.In) || !components.Add(type))
                        Report(Write, Source(parameter), $"Pass '{type.Name}' once as a read-only input.");
                    continue;
                }

                if (parameter.RefKind is not (RefKind.None or RefKind.In or RefKind.Ref))
                    Report(Write, Source(parameter), $"Use ref for writable own components and in for read-only components ('{parameter.Name}').");
                if (!components.Add(type))
                    Report(Write, Source(parameter), $"Declare a single access for '{type.ToDisplayString()}' so references cannot alias.");
            }

            ValidateOwnAccesses(components);
            resourceType = ReadDeclaredInputType("GaTickResource", "runner tick input");
            bufferType = ReadDeclaredInputType("GaReadBuffer", "read buffer element type");
            VisitMethod(entry, Source(entry));
            return diagnostics.ToImmutable();
        }

        private void VisitMethod(IMethodSymbol method, Location callSite)
        {
            if (IsFixedIntrinsic(method))
                return;
            var definition = method.OriginalDefinition;
            if (callChain.Any(active => Equal(active.OriginalDefinition, definition)))
            {
                Report(Unknown, callSite, $"Replace recursive helper '{method.Name}' with a bounded loop.");
                return;
            }

            if (method.IsAsync || method.IsAbstract || method.IsExtern || method.IsVirtual || method.IsOverride ||
                method.ReturnsByRef || method.ReturnsByRefReadonly || !method.ContainingType.IsValueType && !method.IsStatic ||
                method.MethodKind == MethodKind.LocalFunction && !method.IsStatic)
            {
                Report(Managed, callSite, $"Use a closed, nonvirtual value helper for '{method.Name}'.");
                return;
            }

            if (verified.Contains(method) && !method.IsGenericMethod && !method.ContainingType.IsGenericType)
                return;

            var syntax = (definition.PartialImplementationPart ?? definition).DeclaringSyntaxReferences.FirstOrDefault() ??
                definition.AssociatedSymbol?.DeclaringSyntaxReferences.FirstOrDefault();
            var declaration = syntax?.GetSyntax(cancellationToken);
            var body = GetBody(declaration);
            if (body == null)
            {
                Report(Unknown, callSite, $"Provide a source implementation or registered intrinsic for '{method.ToDisplayString()}'.");
                return;
            }

            callChain.Add(method);
            var substitutions = new List<(ITypeParameterSymbol Parameter, ITypeSymbol? Previous)>();
            BindTypes(definition.TypeParameters, method.TypeArguments, substitutions);
            for (INamedTypeSymbol? owner = method.ContainingType; owner != null; owner = owner.ContainingType)
                BindTypes(owner.OriginalDefinition.TypeParameters, owner.TypeArguments, substitutions);

            foreach (var (parameter, _) in substitutions)
                CheckType(typeArguments[parameter], callSite);
            if (!method.ReturnsVoid)
                CheckType(method.ReturnType, Source(definition), allowContext: false);
            foreach (var parameter in definition.Parameters)
                CheckType(parameter.Type, Source(parameter), allowContext: true);
            if (HasUnsafeModifier(declaration!))
                Report(Managed, declaration!.GetLocation(), "Remove unsafe code from the GA call graph.");
            if (declaration is ConstructorDeclarationSyntax constructor)
                VisitConstructorInitialization(constructor, definition.ContainingType);
            Visit(body);

            for (int i = substitutions.Count - 1; i >= 0; i--)
            {
                var (parameter, previous) = substitutions[i];
                if (previous == null)
                    typeArguments.Remove(parameter);
                else
                    typeArguments[parameter] = previous;
            }
            callChain.RemoveAt(callChain.Count - 1);
            verified.Add(method);
        }

        private void BindTypes(ImmutableArray<ITypeParameterSymbol> parameters, ImmutableArray<ITypeSymbol> arguments,
            List<(ITypeParameterSymbol, ITypeSymbol?)> substitutions)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var argument = Resolve(arguments[i]);
                typeArguments.TryGetValue(parameters[i], out var previous);
                substitutions.Add((parameters[i], previous));
                typeArguments[parameters[i]] = argument;
            }
        }

        private ITypeSymbol Resolve(ITypeSymbol type) => type is ITypeParameterSymbol parameter &&
            typeArguments.TryGetValue(parameter, out var argument) ? argument : type;

        private IOperation? GetBody(SyntaxNode? declaration)
        {
            if (declaration == null)
                return null;
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (declaration is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax &&
                model.GetOperation(declaration, cancellationToken) is IMethodBodyOperation methodBody)
                return methodBody.BlockBody ?? methodBody.ExpressionBody;

            SyntaxNode? body = declaration switch
            {
                BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                PropertyDeclarationSyntax property => property.ExpressionBody,
                IndexerDeclarationSyntax indexer => indexer.ExpressionBody,
                ArrowExpressionClauseSyntax expression => expression,
                _ => null
            };
            if (body == null)
                return null;
            var operation = model.GetOperation(body, cancellationToken);
            if (operation != null || body is not ArrowExpressionClauseSyntax arrow)
                return operation;

            var value = arrow.Expression;
            while (value is CheckedExpressionSyntax or ParenthesizedExpressionSyntax)
                value = value is CheckedExpressionSyntax arithmetic ? arithmetic.Expression : ((ParenthesizedExpressionSyntax)value).Expression;
            return model.GetOperation(value, cancellationToken);
        }

        private static bool HasUnsafeModifier(SyntaxNode declaration) => declaration switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            PropertyDeclarationSyntax property => property.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            IndexerDeclarationSyntax indexer => indexer.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            _ => false
        };

        private void VisitConstructorInitialization(ConstructorDeclarationSyntax constructor, INamedTypeSymbol type)
        {
            if (constructor.Initializer is { } delegation)
            {
                var model = compilation.GetSemanticModel(constructor.SyntaxTree);
                var initializerOperation = model.GetOperation(delegation, cancellationToken) ??
                    (model.GetOperation(constructor, cancellationToken) as IConstructorBodyOperation)?.Initializer;
                var call = initializerOperation as IInvocationOperation ??
                    (initializerOperation as IExpressionStatementOperation)?.Operation as IInvocationOperation;
                if (!delegation.IsKind(SyntaxKind.ThisConstructorInitializer) || call == null ||
                    !Equal(call.TargetMethod.ContainingType.OriginalDefinition, type))
                {
                    Report(Unknown, delegation.GetLocation(), "Use a source-defined this(...) constructor on the same value type.");
                    return;
                }

                CheckValueConstructorArguments(call.TargetMethod, call.Arguments, delegation.GetLocation());
                foreach (var argument in call.Arguments.OrderBy(argument => argument.Syntax.SpanStart))
                    Visit(argument);
                if (!call.TargetMethod.IsImplicitlyDeclared)
                {
                    VisitMethod(call.TargetMethod, delegation.GetLocation());
                    return;
                }
            }

            var initializers = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
                .SelectMany(field => field.DeclaringSyntaxReferences)
                .Select(reference => (reference.GetSyntax(cancellationToken) as VariableDeclaratorSyntax)?.Initializer)
                .OfType<EqualsValueClauseSyntax>().ToArray();
            if (initializers.Length == 0)
                return;

            var treeOrder = compilation.SyntaxTrees.Select((tree, index) => (tree, index))
                .ToDictionary(item => item.tree, item => item.index);
            foreach (var initializer in initializers.OrderBy(initializer => treeOrder[initializer.SyntaxTree])
                .ThenBy(initializer => initializer.SpanStart))
            {
                var model = compilation.GetSemanticModel(initializer.SyntaxTree);
                if (model.GetOperation(initializer, cancellationToken) is IFieldInitializerOperation fieldInitializer)
                    Visit(fieldInitializer.Value);
                else
                    Report(Unknown, initializer.GetLocation(), "Use a source-defined instance field initializer.");
            }
        }

        private void CheckValueConstructorArguments(IMethodSymbol constructor, ImmutableArray<IArgumentOperation> arguments, Location location)
        {
            if (constructor.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams) ||
                arguments.Any(argument => argument.ArgumentKind != ArgumentKind.Explicit))
                Report(Unknown, location, "Use explicit by-value arguments for a GA value constructor.");
        }

        private void Report(DiagnosticDescriptor descriptor, Location location, string reason)
        {
            if (!reported.Add((descriptor.Id, location.SourceTree, location.SourceSpan.Start, reason)))
                return;
            var chain = callChain.Count > 1
                ? " Call chain: " + string.Join(" -> ", callChain.Select(method => $"{method.ContainingType.Name}.{method.Name}")) + "."
                : "";
            diagnostics.Add(Diagnostic.Create(descriptor, location, reason, chain));
        }

        private void CheckType(ITypeSymbol type, Location location, bool allowContext = false)
        {
            type = Resolve(type);
            if (Equal(type, context) && allowContext || IsSchema(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
                return;
            var descriptor = type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                ? Arithmetic : type.IsReferenceType ? Managed : Schema;
            Report(descriptor, location, $"Use a registered GA value schema instead of '{type.ToDisplayString()}'.");
        }

        private bool IsSchema(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
        {
            type = Resolve(type);
            if (type.TypeKind == TypeKind.Error || Equal(type, entity) || Equal(type, fixedType))
                return true;
            if (type.SpecialType != SpecialType.None)
                return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Void;
            if (type is not INamedTypeSymbol named)
                return false;
            if (named.TypeKind == TypeKind.Enum)
                return named.EnumUnderlyingType?.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32;
            if (named.TypeKind != TypeKind.Struct || named.IsRefLikeType || named.IsTupleType || !visiting.Add(type))
                return false;

            var fields = named.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
            bool supported = fields.All(field => !field.IsFixedSizeBuffer && field.RefKind == RefKind.None &&
                !field.IsImplicitlyDeclared && IsSchema(field.Type, visiting));
            visiting.Remove(type);
            return supported && !named.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
                attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int layout && layout != 0);
        }
    }
}
