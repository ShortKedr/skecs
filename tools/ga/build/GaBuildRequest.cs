using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SKECS.GA;

internal sealed class GaBuildRequest
{
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public string SourceRoot { get; init; } = ".";
    public required string[] Sources { get; init; }
    public string[] References { get; init; } = [];
    public string[] ReferenceDirectories { get; init; } = [];
    public required string SystemId { get; init; }
    public required string SimulationContractId { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ArtifactName { get; init; }
    public string AdapterClass { get; init; } = "GaCpuAdapter";
    public GaPolicy Policy { get; init; } = GaPolicy.Auto;
    public string? SlangAdapter { get; init; }
    public string? SlangCompilerExecutable { get; init; }
    public string Target { get; init; } = "vulkan";
    public int WorkgroupX { get; init; } = GaSlangModule.ThreadsPerGroup;
    public string? DxcDirectory { get; init; }
    public string? FxcDirectory { get; init; }
    public string? SpirvCrossExecutable { get; init; }
    public bool CheckOverflow { get; init; }
    public string LanguageVersion { get; init; } = "12.0";
    public string[] Defines { get; init; } = [];
    public NullableContextOptions Nullable { get; init; } = NullableContextOptions.Disable;
    public bool AllowUnsafe { get; init; }
    public string AssemblyName { get; init; } = "GaConsumer";
    public OutputKind OutputKind { get; init; } = OutputKind.DynamicallyLinkedLibrary;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(SystemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SimulationContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ArtifactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AssemblyName);
        if (SystemId.Any(char.IsControl) || SimulationContractId.Any(char.IsControl))
            throw new ArgumentException("GA contract and system IDs cannot contain control characters.");
        if (Sources is not { Length: > 0 } || References is null || ReferenceDirectories is null || Defines is null)
            throw new ArgumentException("A GA request needs source files and valid reference and define lists.");
        if (ArtifactName.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')))
            throw new ArgumentException("artifactName must contain only ASCII letters, digits, underscores, and hyphens.");
        if (!SyntaxFacts.IsValidIdentifier(AdapterClass) || AdapterClass.StartsWith('@'))
            throw new ArgumentException("adapterClass must be an unescaped C# class identifier.");
        if (Defines.Any(define => string.IsNullOrEmpty(define) || !SyntaxFacts.IsValidIdentifier(define)))
            throw new ArgumentException("defines must be C# preprocessor identifiers.");
        if (SlangAdapter is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SlangAdapter);
        if (SlangCompilerExecutable is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SlangCompilerExecutable);
        if (Target is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "webgpu"))
            throw new ArgumentException("target must be vulkan, d3d12, d3d11, opengl, gles, or webgpu.");
        if (WorkgroupX < 1 ||
            ((Target is "d3d12" or "d3d11") && WorkgroupX > GaSlangEmitter.MaximumD3D12WorkgroupX))
            throw new ArgumentException("workgroupX must be positive and within the target's group size limit.");
        if (DxcDirectory is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(DxcDirectory);
        if (FxcDirectory is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(FxcDirectory);
        if (SpirvCrossExecutable is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SpirvCrossExecutable);
    }

    public CSharpCompilation CreateCompilation(string requestDirectory)
    {
        if (!LanguageVersionFacts.TryParse(LanguageVersion, out var languageVersion))
            throw new ArgumentException($"Unknown C# language version '{LanguageVersion}'.");
        string sourceRoot = Path.GetFullPath(SourceRoot, requestDirectory);
        var parseOptions = new CSharpParseOptions(languageVersion, preprocessorSymbols: Defines);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var trees = new List<SyntaxTree>(Sources.Length);
        foreach (string source in Sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            if (Path.IsPathRooted(source))
                throw new ArgumentException($"GA source paths must be relative to sourceRoot: '{source}'.");
            string physicalPath = Path.GetFullPath(source, sourceRoot);
            string logicalPath = Path.GetRelativePath(sourceRoot, physicalPath).Replace('\\', '/');
            if (logicalPath == ".." || logicalPath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(logicalPath))
                throw new ArgumentException($"GA source is outside sourceRoot: '{source}'.");
            if (!paths.Add(logicalPath))
                throw new ArgumentException($"Duplicate GA source '{logicalPath}'.");
            var text = SourceText.From(File.ReadAllText(physicalPath, new UTF8Encoding(false, true)), Encoding.UTF8);
            trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, logicalPath));
        }

        var referencePaths = References.Select(path => Path.GetFullPath(path, requestDirectory)).Concat(
            ReferenceDirectories.SelectMany(path => Directory.EnumerateFiles(Path.GetFullPath(path, requestDirectory), "*.dll")));
        var references = referencePaths.Distinct(paths.Comparer).Order(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        if (references.Length == 0)
            throw new ArgumentException("Supply the consumer's metadata references, including a framework reference pack.");
        var options = new CSharpCompilationOptions(OutputKind, checkOverflow: CheckOverflow, allowUnsafe: AllowUnsafe,
            deterministic: true, nullableContextOptions: Nullable);
        return CSharpCompilation.Create(AssemblyName, trees, references, options);
    }

    public IMethodSymbol FindExecute(CSharpCompilation compilation)
    {
        var marker = compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute")
            ?? throw new ArgumentException("Supply a reference that defines SKECS.GA.SecsSystemAttribute.");
        var declared = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol { TypeKind: TypeKind.Struct } type)
                    declared.Add(type);
            }
        }

        var matches = declared.Where(type => type.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker) &&
            SystemId == (attribute.NamedArguments.FirstOrDefault(arg => arg.Key == "SystemId").Value.Value as string ??
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))).ToArray();
        if (matches.Length != 1)
            throw new ArgumentException($"Expected exactly one [SecsSystem] with SystemId '{SystemId}', found {matches.Length}.");
        var entries = matches[0].GetMembers("Execute").OfType<IMethodSymbol>().Where(method =>
            !method.IsStatic && method.ReturnsVoid && !method.IsGenericMethod && method.DeclaringSyntaxReferences.Length == 1)
            .ToArray();
        if (entries.Length != 1)
            throw new ArgumentException($"Expected exactly one source instance void Execute for '{SystemId}', found {entries.Length}.");
        return entries[0];
    }
}
