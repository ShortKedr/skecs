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
        private readonly INamedTypeSymbol? fixedType;
        private readonly List<IMethodSymbol> callChain = new();
        private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> verified = new(SymbolEqualityComparer.Default);
        private readonly HashSet<(string Id, SyntaxTree? Tree, int Start, string Reason)> reported = new();
        private readonly ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        public Verifier(Compilation compilation, IMethodSymbol entry, CancellationToken cancellationToken)
        {
            this.compilation = compilation;
            this.entry = entry;
            this.cancellationToken = cancellationToken;
            entity = compilation.GetTypeByMetadataName("SKECS.Entity");
            context = compilation.GetTypeByMetadataName("SKECS.GA.SystemContext");
            fixedType = compilation.GetTypeByMetadataName("SKECS.Numerics.Fixed");
        }

        public ImmutableArray<Diagnostic> Run()
        {
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
            if (declaration is ConstructorDeclarationSyntax constructor && HasHiddenConstruction(constructor, definition.ContainingType))
                Report(Unknown, constructor.GetLocation(), "Use explicit value assignments in a GA constructor instead of constructor chaining or field initializers.");
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
            SyntaxNode? body = declaration switch
            {
                BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
                PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
                ArrowExpressionClauseSyntax expression => expression.Expression,
                _ => null
            };
            return body == null ? null : compilation.GetSemanticModel(body.SyntaxTree).GetOperation(body, cancellationToken);
        }

        private static bool HasUnsafeModifier(SyntaxNode declaration) => declaration switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            PropertyDeclarationSyntax property => property.Modifiers.Any(SyntaxKind.UnsafeKeyword),
            _ => false
        };

        private bool HasHiddenConstruction(ConstructorDeclarationSyntax constructor, INamedTypeSymbol type) =>
            constructor.Initializer != null || type.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic &&
                field.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: not null }));

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
