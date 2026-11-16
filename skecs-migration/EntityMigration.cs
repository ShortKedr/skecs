using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Skecs.Migration;

internal sealed class EntityMigration
{
    private readonly CSharpCompilation compilation;
    private readonly IAssemblySymbol oldAssembly;
    private readonly Diagnostic? compilationError;

    public EntityMigration(string package, IReadOnlyList<SyntaxTree> sources, bool implicitUsings)
    {
        using var archive = ZipFile.OpenRead(package);
        var entry = archive.GetEntry("lib/net8.0/SECS.dll")
            ?? throw new InvalidOperationException("The old package must contain lib/net8.0/SECS.dll.");
        using var contents = entry.Open();
        using var bytes = new MemoryStream();
        contents.CopyTo(bytes);
        var reference = MetadataReference.CreateFromImage(bytes.ToArray());
        var runtime = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The .NET runtime references could not be found.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == runtime)
            .Select(path => MetadataReference.CreateFromFile(path));
        var trees = implicitUsings
            ? sources.Append(CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; " +
                "global using System.Linq; global using System.Net.Http; global using System.Threading; " +
                "global using System.Threading.Tasks;", new CSharpParseOptions(LanguageVersion.Preview)))
            : sources;
        var output = sources.Any(tree => tree.GetRoot().ChildNodes().Any(node => node is GlobalStatementSyntax))
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;
        compilation = CSharpCompilation.Create("MigrationAnalysis", trees, references.Append(reference),
            new CSharpCompilationOptions(output, allowUnsafe: true));
        oldAssembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol
            ?? throw new InvalidOperationException("The old package SECS assembly could not be read.");
        if (oldAssembly.Identity.Name != "SECS" || oldAssembly.Identity.Version != new Version(0, 1, 0, 0))
            throw new InvalidOperationException("The old package must contain SECS assembly version 0.1.0.0.");
        compilationError = compilation.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    public string Review(SyntaxTree tree, Action<SyntaxNode, string> report)
    {
        var model = compilation.GetSemanticModel(tree);
        var edits = new List<TextChange>();
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case BaseObjectCreationExpressionSyntax creation
                    when model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor &&
                         IsOldType(constructor.ContainingType, "Entity") && constructor.Parameters.Length == 2:
                    if (compilationError is null && creation is ObjectCreationExpressionSyntax { Initializer: null } explicitCreation &&
                        TryGetChunkOwner(explicitCreation, model) is { } world)
                    {
                        var prefix = TextSpan.FromBounds(creation.SpanStart, explicitCreation.Type.Span.End);
                        if (!creation.DescendantTrivia(prefix).Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
                        {
                            edits.Add(new TextChange(prefix, world.Identifier.Text + ".BindEntity"));
                            break;
                        }
                    }
                    report(creation, "Review this Entity construction. Use BindEntity on its owning World when the handle must be bound." +
                        (compilationError is null ? "" : $" Ownership could not be checked because scoped analysis reported {compilationError.Id}: {compilationError.GetMessage()}"));
                    break;
                case MemberAccessExpressionSyntax access
                    when model.GetSymbolInfo(access).Symbol is IPropertySymbol property &&
                         IsOldType(property.ContainingType, "Entity") && property.Name == "IsValid":
                    report(access, "Review Entity.IsValid. Use the owning World's IsAlive for liveness.");
                    break;
                case ExpressionSyntax expression
                    when expression is BinaryExpressionSyntax or InvocationExpressionSyntax &&
                         model.GetSymbolInfo(expression).Symbol is IMethodSymbol method &&
                         IsOldType(method.ContainingType, "Entity") &&
                         method.Name is "op_Equality" or "op_Inequality" or "Equals" or "GetHashCode":
                    report(expression, "Review Entity equality or hashing. SKECS includes the owning world in identity.");
                    break;
            }
        }
        return tree.GetText().WithChanges(edits).ToString();
    }

    public static void ReviewUnverified(SyntaxNode root, Action<SyntaxNode, string> report)
    {
        var nodes = root.DescendantNodes().ToArray();
        var imported = nodes.OfType<UsingDirectiveSyntax>().Any(directive => directive.Alias is null && IsOldNamespace(directive.Name));
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in nodes.OfType<VariableDeclarationSyntax>())
        {
            foreach (var variable in declaration.Variables)
            {
                if (IsPossibleEntity(declaration.Type) ||
                    variable.Initializer?.Value is ObjectCreationExpressionSyntax creation && IsPossibleEntity(creation.Type))
                    variables.Add(variable.Identifier.ValueText);
            }
        }
        foreach (var parameter in nodes.OfType<ParameterSyntax>())
            if (IsPossibleEntity(parameter.Type)) variables.Add(parameter.Identifier.ValueText);

        foreach (var node in nodes)
        {
            switch (node)
            {
                case ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 2 } creation
                    when IsPossibleEntity(creation.Type):
                    report(creation, "Unverified Entity construction. Confirm the owning World or supply --old-package for symbol analysis.");
                    break;
                case MemberAccessExpressionSyntax access when IsPossibleVariable(access.Expression) &&
                    access.Name.Identifier.ValueText is "IsValid" or "Equals" or "GetHashCode":
                    report(access, "Unverified Entity API use. Review liveness, equality or hashing as applicable.");
                    break;
                case BinaryExpressionSyntax comparison when
                    (comparison.IsKind(SyntaxKind.EqualsExpression) || comparison.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    (IsPossibleVariable(comparison.Left) || IsPossibleVariable(comparison.Right)):
                    report(comparison, "Unverified Entity comparison. Review world ownership in equality.");
                    break;
            }
        }

        bool IsPossibleEntity(TypeSyntax? type) => type switch
        {
            QualifiedNameSyntax { Right.Identifier.ValueText: "Entity" } qualified => IsOldNamespace(qualified.Left),
            IdentifierNameSyntax { Identifier.ValueText: "Entity" } => imported,
            _ => false
        };
        bool IsPossibleVariable(ExpressionSyntax expression) =>
            expression is IdentifierNameSyntax name && variables.Contains(name.Identifier.ValueText);
    }

    private static bool IsOldNamespace(NameSyntax? name) => name is IdentifierNameSyntax { Identifier.ValueText: "SECS" } or
        AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global", Name.Identifier.ValueText: "SECS" };

    private IdentifierNameSyntax? TryGetChunkOwner(ObjectCreationExpressionSyntax creation, SemanticModel model)
    {
        // A named or computed coordinate can have different evaluation order or provenance.
        if (creation.ArgumentList is not { Arguments.Count: 2 } list ||
            list.Arguments.Any(argument => argument.NameColon is not null) ||
            list.Arguments[0].Expression is not ElementAccessExpressionSyntax id ||
            list.Arguments[1].Expression is not ElementAccessExpressionSyntax version ||
            id.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax chunk } idArray ||
            version.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax } versionArray ||
            model.GetSymbolInfo(idArray).Symbol is not IPropertySymbol { Name: "Entities" } entities ||
            model.GetSymbolInfo(versionArray).Symbol is not IPropertySymbol { Name: "Versions" } versions ||
            !IsOldType(entities.ContainingType, "QueryChunkView") ||
            !SymbolEqualityComparer.Default.Equals(entities.ContainingType, versions.ContainingType) ||
            !MatchingIndices(id, version, model))
            return null;

        if (model.GetSymbolInfo(chunk).Symbol is not ILocalSymbol chunkLocal ||
            !SymbolEqualityComparer.Default.Equals(chunkLocal, model.GetSymbolInfo(versionArray.Expression).Symbol) ||
            !SymbolEqualityComparer.Default.Equals(chunkLocal, model.LookupSymbols(creation.SpanStart, name: chunk.Identifier.ValueText).SingleOrDefault()))
            return null;
        var loop = creation.Ancestors().OfType<ForEachStatementSyntax>()
            .FirstOrDefault(statement => SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(statement), chunkLocal));
        if (loop?.Expression is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Chunks",
                    Expression: InvocationExpressionSyntax query
                }
            } chunks ||
            model.GetSymbolInfo(chunks).Symbol is not IMethodSymbol chunksMethod ||
            !IsOldType(chunksMethod.ContainingType, "Query") ||
            query.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax world } ||
            model.GetSymbolInfo(query).Symbol is not IMethodSymbol { Name: "Query" } queryMethod ||
            !IsOldType(queryMethod.ContainingType, "World"))
            return null;
        if (model.GetSymbolInfo(world).Symbol is not ILocalSymbol owner ||
            owner.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax().Parent?.Parent is not LocalDeclarationStatementSyntax declaration ||
            !declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) ||
            !SymbolEqualityComparer.Default.Equals(owner, model.LookupSymbols(creation.SpanStart, name: world.Identifier.ValueText).SingleOrDefault()))
            return null;

        return world;
    }

    private bool IsOldType(INamedTypeSymbol type, string name) => type.Name == name &&
        type.ContainingNamespace.ToDisplayString() == "SECS" &&
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, oldAssembly);

    private static bool MatchingIndices(ElementAccessExpressionSyntax left, ElementAccessExpressionSyntax right, SemanticModel model)
    {
        if (left.ArgumentList.Arguments.Count != 1 || right.ArgumentList.Arguments.Count != 1) return false;
        var first = left.ArgumentList.Arguments[0].Expression;
        var second = right.ArgumentList.Arguments[0].Expression;
        if (first is LiteralExpressionSyntax && second is LiteralExpressionSyntax)
            return SyntaxFactory.AreEquivalent(first, second);
        return first is IdentifierNameSyntax && second is IdentifierNameSyntax &&
               model.GetSymbolInfo(first).Symbol is ILocalSymbol { RefKind: RefKind.None } index &&
               SymbolEqualityComparer.Default.Equals(index, model.GetSymbolInfo(second).Symbol);
    }
}
