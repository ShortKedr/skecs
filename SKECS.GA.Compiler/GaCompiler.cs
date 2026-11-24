using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SKECS.GA.Analysis;

namespace SKECS.GA;

public sealed record GaCompilationResult(GaProgram? Program, ImmutableArray<Diagnostic> Diagnostics);

public static class GaCompiler
{
    private static readonly DiagnosticDescriptor Unsupported = new(
        "GA0100", "Operation has no GA IR lowering", "{0}", "SKECS.GA", DiagnosticSeverity.Error, true);

    public static GaCompilationResult Compile(CSharpCompilation compilation, IMethodSymbol method,
        CancellationToken cancellationToken = default) => Compile(compilation, method, null, cancellationToken);

    /// <summary>Compiles with the previous generated declarations available for symbol binding.</summary>
    /// <param name="compilation">The consumer compilation including the previous generated sources.</param>
    /// <param name="method">The authored GA entry point in the compilation.</param>
    /// <param name="previousGeneratedSources">
    /// The exact syntax trees owned by the generator. C# errors located entirely in these trees are skipped.
    /// The caller must validate the compilation with the replacement generated sources.
    /// </param>
    /// <param name="cancellationToken">Cancellation for compilation and analysis.</param>
    public static GaCompilationResult CompileForRegeneration(CSharpCompilation compilation, IMethodSymbol method,
        IReadOnlyCollection<SyntaxTree> previousGeneratedSources, CancellationToken cancellationToken = default)
    {
        if (previousGeneratedSources is null) throw new ArgumentNullException(nameof(previousGeneratedSources));
        return Compile(compilation, method, previousGeneratedSources, cancellationToken);
    }

    private static GaCompilationResult Compile(CSharpCompilation compilation, IMethodSymbol method,
        IReadOnlyCollection<SyntaxTree>? previousGeneratedSources,
        CancellationToken cancellationToken)
    {
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));
        if (method is null) throw new ArgumentNullException(nameof(method));
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error &&
                (previousGeneratedSources is null || !IsInGeneratedSources(d, previousGeneratedSources)))
            .ToImmutableArray();
        if (!errors.IsEmpty)
            return new GaCompilationResult(null, errors);

        var diagnostics = GaSemanticAnalysis.Analyze(method, compilation, cancellationToken);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new GaCompilationResult(null, diagnostics);

        try
        {
            var attribute = method.ContainingType.GetAttributes()
                .SingleOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass,
                    compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute")));
            if (attribute == null || method.Name != "Execute" || method.IsStatic || !method.ReturnsVoid
                || method.IsGenericMethod || method.DeclaringSyntaxReferences.Length != 1)
                return Reject(method.Locations.FirstOrDefault(), "A GA entry point must be a marked instance Execute method.");

            var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (model.GetOperation(declaration, cancellationToken) is not IMethodBodyOperation body)
                return Reject(declaration.GetLocation(), "The Execute body must be available as C# source.");

            var initial = body.BlockBody ?? body.ExpressionBody!;
            string systemId = attribute.NamedArguments.FirstOrDefault(p => p.Key == "SystemId").Value.Value as string
                ?? method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.IsNullOrWhiteSpace(systemId))
                return Reject(declaration.GetLocation(), "SystemId cannot be empty.");
            var sourceParts = method.ContainingType.DeclaringSyntaxReferences.Select(reference =>
                GaCompilerText.NormalizeLineEndings(reference.GetSyntax(cancellationToken).ToFullString()))
                .OrderBy(source => source, StringComparer.Ordinal);
            var sourceDigest = GaArtifactManifest.ComputeDigest(Encoding.UTF8.GetBytes(string.Join("\n", sourceParts)));
            return new GaCompilationResult(new GaLowerer(compilation, method, cancellationToken)
                .Lower(systemId, sourceDigest, initial), diagnostics);
        }
        catch (GaLoweringException error)
        {
            return Reject(error.Location, error.Message);
        }

        GaCompilationResult Reject(Location? location, string error) => new(null,
            diagnostics.Add(Diagnostic.Create(Unsupported, location, error)));
    }

    private static bool IsInGeneratedSources(Diagnostic diagnostic, IReadOnlyCollection<SyntaxTree> generatedSources)
    {
        return IsGeneratedLocation(diagnostic.Location) && diagnostic.AdditionalLocations.All(IsGeneratedLocation);

        bool IsGeneratedLocation(Location location) => location.SourceTree is { } tree && generatedSources.Contains(tree);
    }
}

internal sealed class GaLoweringException(Location location, string message) : Exception(message)
{
    internal Location Location { get; } = location;
}
