using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.Rendering;
using UnityEngine;
using static SKECS.GA.GaUnityComputeAbi;

namespace SKECS.GA.Unity.Editor
{
    /// <summary>Generates compute assets and registration for the active Unity Player target.</summary>
    public sealed class GaUnityGenerator : IPreprocessBuildWithReport
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string[] GeneratedSources = { ".cpu.g.cs", ".row.g.cs", ".registration.g.cs" };
        private static readonly string[] GeneratedData =
            { ".compute", ".compute.bytes", ".manifest.bytes", ".layout.bytes", ".source-map.bytes", ".configuration.bytes" };
        private static readonly string[] ReservedNames = { "Register", "Contract", "LoadAsset", "CreateRequest" };

        [Serializable]
        private sealed class Settings
        {
            public string source = "";
            public string systemType = "";
            public string simulationContract = "";
            public string adapter = "";
            public string inputType = "";
            public int workgroup = 64;
        }

        public int callbackOrder => 0;

        [MenuItem("Tools/SKECS GA/Generate Player Assets")]
        public static void GenerateAll()
        {
            var products = PrepareProducts(out var obsolete);
            foreach (var product in products)
                product.WriteFiles();
            foreach (string path in obsolete)
                if (File.Exists(path) && !AssetDatabase.DeleteAsset(path))
                    throw new IOException($"Unity could not remove the obsolete generated asset '{path}'.");
            foreach (var product in products)
                product.ValidateImport(generate: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"SKECS GA generated {products.Count} Unity kernel asset(s) for {EditorUserBuildSettings.activeBuildTarget}.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                var products = PrepareProducts(out var obsolete);
                if (obsolete.Count != 0)
                    throw new InvalidDataException($"Obsolete generated asset '{obsolete[0]}'.");
                if (products.Count > 0 && report.summary.platform != EditorUserBuildSettings.activeBuildTarget)
                    throw new InvalidDataException("Switch the active Player build target before generating Unity GA assets.");
                foreach (var product in products)
                    product.ValidateImport(generate: false);
            }
            catch (Exception error)
            {
                throw new BuildFailedException($"Unity GA asset validation failed. Run Tools > SKECS GA > Generate Player Assets. {error.Message}");
            }
        }

        private static List<Product> PrepareProducts(out List<string> obsolete)
        {
            var paths = AssetDatabase.GetAllAssetPaths();
            var settingsPaths = paths.Where(path =>
                path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".skecsga.json", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var products = new List<Product>(settingsPaths.Length);
            var previousProducts = FindPreviousProducts(paths);
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var compilations = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
            var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generated = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in previousProducts)
                foreach (string suffix in GeneratedSources)
                    generated.Add(item.Stem + suffix);

            var requests = new List<(string Path, string Directory, Settings Value)>(settingsPaths.Length);
            foreach (string settingsPath in settingsPaths)
            {
                var settings = JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath, Utf8));
                var directory = Path.GetDirectoryName(settingsPath).Replace('\\', '/');
                if (settings is null || string.IsNullOrEmpty(settings.source) || string.IsNullOrEmpty(settings.systemType) ||
                    string.IsNullOrWhiteSpace(settings.simulationContract) || settings.simulationContract.Any(char.IsControl) ||
                    !SyntaxFacts.IsValidIdentifier(settings.adapter ?? "") || settings.adapter.StartsWith("@", StringComparison.Ordinal) ||
                    ReservedNames.Contains(settings.adapter, StringComparer.Ordinal) ||
                    settings.workgroup < 1 || Path.IsPathRooted(settings.source))
                    throw new InvalidDataException($"Invalid Unity GA generation settings '{settingsPath}'.");
                requests.Add((settingsPath, directory, settings));
                foreach (string suffix in GeneratedSources)
                    generated.Add(Stem(directory, settings.adapter) + suffix);
            }

            foreach (var (settingsPath, directory, settings) in requests)
            {
                string sourcePath = LogicalPath(Path.Combine(directory, settings.source));
                if (!sourcePath.StartsWith(directory + "/", StringComparison.Ordinal) ||
                    !sourcePath.EndsWith(".cs", StringComparison.Ordinal) || !File.Exists(sourcePath))
                    throw new InvalidDataException($"GA source in '{settingsPath}' must be a C# file under '{directory}'.");
                string assemblyName = Path.GetFileNameWithoutExtension(CompilationPipeline.GetAssemblyNameFromScriptPath(sourcePath));
                var assembly = assemblies.SingleOrDefault(item => item.name == assemblyName)
                    ?? throw new InvalidDataException($"'{sourcePath}' must be in a Player assembly for the active target.");
                string sourceDefinition = CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(sourcePath);
                string settingsDefinition = CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(directory + "/GaGenerated.cs");
                if (sourceDefinition != settingsDefinition)
                    throw new InvalidDataException($"Place '{settingsPath}' in the same Player assembly as '{sourcePath}'.");
                if (!outputNames.Add(assemblyName + "/" + settings.adapter))
                    throw new InvalidDataException($"Duplicate Unity GA adapter name '{settings.adapter}' in '{assemblyName}'.");

                if (!compilations.TryGetValue(assembly.name, out var compilation))
                {
                    compilation = CreateCompilation(assembly);
                    compilations.Add(assembly.name, compilation);
                }
                var type = compilation.GetTypeByMetadataName(settings.systemType);
                if (type is null || !type.Locations.Any(location => location.SourceTree?.FilePath == sourcePath))
                    throw new InvalidDataException($"'{settings.systemType}' is not declared in '{sourcePath}'.");
                var entries = type.GetMembers("Execute").OfType<IMethodSymbol>().Where(method =>
                    !method.IsStatic && method.ReturnsVoid && !method.IsGenericMethod && method.DeclaringSyntaxReferences.Length == 1).ToArray();
                if (entries.Length != 1)
                    throw new InvalidDataException($"'{settings.systemType}' must have one instance void Execute method.");
                var previous = compilation.SyntaxTrees.Where(tree => generated.Contains(tree.FilePath)).ToArray();
                var lowered = GaCompiler.CompileForRegeneration(compilation, entries[0], previous);
                if (lowered.Program is null)
                    throw new InvalidDataException($"GA could not compile '{settingsPath}':\n{string.Join("\n", lowered.Diagnostics)}");

                var program = lowered.Program;
                string input = ResolveInput(compilation, program, settings.inputType);
                var bundle = GaUnityComputePackager.Create(program, settings.simulationContract, settings.workgroup, Application.unityVersion);
                products.Add(new Product(settings, settingsPath, directory, input, program, bundle, compilation));
            }

            var activeOutputs = new HashSet<string>(products.SelectMany(product => product.OutputPaths), StringComparer.Ordinal);
            obsolete = new List<string>();
            foreach (var item in previousProducts)
            {
                foreach (string path in GeneratedSources.Concat(GeneratedData).Select(suffix => item.Stem + suffix).Append(item.Asset))
                    if (!activeOutputs.Contains(path) && File.Exists(path))
                        obsolete.Add(path);
            }

            foreach (var entry in compilations)
            {
                var compilation = entry.Value;
                var parse = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
                var previous = compilation.SyntaxTrees.Where(tree => generated.Contains(tree.FilePath));
                var replacement = products.Where(product => product.AssemblyName == entry.Key)
                    .SelectMany(product => product.GeneratedCSharp(parse));
                var errors = compilation.RemoveSyntaxTrees(previous).AddSyntaxTrees(replacement).GetDiagnostics()
                    .Where(error => error.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length != 0)
                    throw new InvalidDataException($"The generated Unity GA assets do not compile in '{entry.Key}':\n{string.Join("\n", errors.Select(error => error.ToString()))}");
            }
            return products;
        }

        private static List<(string Stem, string Asset)> FindPreviousProducts(IEnumerable<string> paths)
        {
            var products = new List<(string Stem, string Asset)>();
            const string resourceFolder = "/Resources/SKECS.GA/";
            foreach (string path in paths)
            {
                int root = path.LastIndexOf(resourceFolder, StringComparison.Ordinal);
                if (root < 0 || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !path.EndsWith(".asset", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) ||
                    AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(GaUnityKernelAsset))
                    continue;
                string directory = path.Substring(0, root);
                string name = Path.GetFileName(directory);
                if (Path.GetFileName(Path.GetDirectoryName(directory)) == "Generated")
                    products.Add((directory + "/" + name, path));
            }
            return products;
        }

        private static CSharpCompilation CreateCompilation(UnityEditor.Compilation.Assembly assembly)
        {
            string root = Directory.GetCurrentDirectory();
            var compilerOptions = assembly.compilerOptions;
            var responseFiles = compilerOptions.ResponseFiles ?? Array.Empty<string>();
            var args = responseFiles.Select(path => "@" + Path.GetFullPath(path))
                .Concat(compilerOptions.AdditionalCompilerArguments ?? Array.Empty<string>());
            var command = CSharpCommandLineParser.Default.Parse(args.Concat(assembly.sourceFiles.Take(1)), root, null);
            var errors = command.Errors.Where(error => error.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0)
                throw new InvalidDataException($"Invalid Player compiler options for '{assembly.name}': {string.Join("\n", errors.Select(error => error.ToString()))}");

            var defines = assembly.defines.Concat(command.ParseOptions.PreprocessorSymbolNames).Distinct();
            var parse = command.ParseOptions.WithPreprocessorSymbols(defines);
            if (parse.SpecifiedLanguageVersion == LanguageVersion.Default)
            {
                if (!LanguageVersionFacts.TryParse(compilerOptions.LanguageVersion, out var language) || language == LanguageVersion.Default)
                    language = LanguageVersion.CSharp9;
                parse = parse.WithLanguageVersion(language);
            }
            var trees = assembly.sourceFiles.OrderBy(path => path, StringComparer.Ordinal).Select(file =>
                CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file, Utf8), Utf8), parse, LogicalPath(file)));
            var references = assembly.allReferences.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path))
                .Concat(command.MetadataReferences.Select(reference =>
                    MetadataReference.CreateFromFile(Path.GetFullPath(reference.Reference), reference.Properties)));
            var options = command.CompilationOptions.WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(command.CompilationOptions.AllowUnsafe || compilerOptions.AllowUnsafeCode);
            return CSharpCompilation.Create(assembly.name, trees, references, options);
        }

        private static byte[] CompilationStamp(CSharpCompilation compilation)
        {
            var parse = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
            var defines = parse.PreprocessorSymbolNames.OrderBy(symbol => symbol, StringComparer.Ordinal);
            var referenceNames = compilation.ReferencedAssemblyNames.Select(name => name.ToString()).OrderBy(name => name, StringComparer.Ordinal);
            return Utf8.GetBytes($"{Application.unityVersion}\n{EditorUserBuildSettings.activeBuildTarget}\n" +
                $"{typeof(CSharpCompilation).Assembly.GetName().Version}\n{parse.LanguageVersion}\n" +
                $"overflow={compilation.Options.CheckOverflow}\nunsafe={compilation.Options.AllowUnsafe}\n" +
                $"nullable={compilation.Options.NullableContextOptions}\n" +
                string.Join("\n", defines) + "\n\n" + string.Join("\n", referenceNames) + "\n");
        }

        private static string LogicalPath(string path) =>
            Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path)).Replace('\\', '/');

        private static string Stem(string directory, string name) => $"{directory}/Generated/{name}/{name}";

        private static string ResolveInput(CSharpCompilation compilation, GaProgram program, string configured)
        {
            if (string.IsNullOrEmpty(configured))
                return program.TickResourceType;
            var type = compilation.GetTypeByMetadataName(configured);
            var name = type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (type is null || !type.IsUnmanagedType || (program.TickResourceType is not null && program.TickResourceType != name))
                throw new InvalidDataException($"GA input '{configured}' must be unmanaged and match the declared tick resource.");
            return name;
        }

        private sealed class Product
        {
            private readonly string settingsPath;
            private readonly string stem;
            private readonly string assetPath;
            private readonly GaArtifactBundle bundle;
            private readonly Dictionary<string, byte[]> files;

            internal Product(Settings settings, string settingsPath, string directory, string input, GaProgram program,
                GaArtifactBundle bundle, CSharpCompilation compilation)
            {
                this.settingsPath = settingsPath;
                this.bundle = bundle;
                AssemblyName = compilation.AssemblyName;
                string name = settings.adapter;
                string resource = "SKECS.GA/" + AssetDatabase.AssetPathToGUID(settingsPath);
                stem = Stem(directory, name);
                assetPath = $"{directory}/Generated/{name}/Resources/{resource}.asset";
                files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    [stem + ".compute"] = bundle.Artifact.ToArray(),
                    [Sidecar("compute")] = bundle.Artifact.ToArray(),
                    [Sidecar("manifest")] = bundle.Manifest.GetCanonicalBytes(),
                    [Sidecar("layout")] = bundle.Reflection.ToArray(),
                    [Sidecar("source-map")] = bundle.SourceMap.ToArray(),
                    [Sidecar("configuration")] = CompilationStamp(compilation),
                    [stem + ".cpu.g.cs"] = SourceBytes(GaCpuAdapterEmitter.EmitUnity(program, name + "Cpu")),
                    [stem + ".row.g.cs"] = SourceBytes(GaRowCodecEmitter.EmitUnity(program, name + "Rows")),
                    [stem + ".registration.g.cs"] = SourceBytes(GaUnityRegistration.Emit(program, bundle.Manifest.Contract, name, input, resource))
                };
            }

            internal string AssemblyName { get; }
            internal IEnumerable<string> OutputPaths => files.Keys.Append(assetPath);

            internal IEnumerable<SyntaxTree> GeneratedCSharp(CSharpParseOptions parse) =>
                GeneratedSources.Select(suffix => stem + suffix).Select(path =>
                    CSharpSyntaxTree.ParseText(SourceText.From(Utf8.GetString(files[path]), Utf8), parse, path));

            private string Sidecar(string suffix) => $"{stem}.{suffix}.bytes";

            internal void WriteFiles()
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stem));
                foreach (var file in files)
                    if (!Matches(file.Key, file.Value))
                        File.WriteAllBytes(file.Key, file.Value);
            }

            internal void ValidateImport(bool generate)
            {
                string shaderPath = stem + ".compute";
                if (generate)
                {
                    AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                    foreach (string path in files.Keys.Where(path => path.EndsWith(".bytes", StringComparison.Ordinal)))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                else
                {
                    foreach (var file in files)
                        if (!Matches(file.Key, file.Value))
                            throw new InvalidDataException($"'{file.Key}' is missing or stale for '{settingsPath}'.");
                }

                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
                if (!shader)
                    throw new InvalidDataException($"ComputeShader import failed for '{shaderPath}'.");
                var errors = ShaderUtil.GetComputeShaderMessages(shader)
                    .Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
                if (errors.Length != 0)
                    throw new InvalidDataException($"Unity shader import failed for '{shaderPath}':\n{string.Join("\n", errors.Select(message => message.message))}");
                GaUnityDevice.ValidateArtifact(bundle);
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && SystemInfo.supportsComputeShaders)
                {
                    uint group = (uint)bundle.Manifest.Requirements.WorkgroupX;
                    GaUnityDevice.ValidateKernel(shader, MainKernel, group);
                    GaUnityDevice.ValidateKernel(shader, ProbeKernel, group);
                }

                var source = LoadSidecar("compute");
                var manifest = LoadSidecar("manifest");
                var layout = LoadSidecar("layout");
                var positions = LoadSidecar("source-map");
                var asset = AssetDatabase.LoadAssetAtPath<GaUnityKernelAsset>(assetPath);
                string contentId = bundle.ContentId;
                if (asset && asset.Matches(shader, contentId, source, manifest, layout, positions))
                    return;
                if (!generate)
                    throw new InvalidDataException($"The imported kernel asset '{assetPath}' is missing or stale.");
                if (!asset)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    asset = ScriptableObject.CreateInstance<GaUnityKernelAsset>();
                    asset.Assign(shader, contentId, source, manifest, layout, positions);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                else
                {
                    asset.Assign(shader, contentId, source, manifest, layout, positions);
                    EditorUtility.SetDirty(asset);
                }
            }

            private TextAsset LoadSidecar(string suffix)
            {
                string path = Sidecar(suffix);
                var sidecar = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (!sidecar || !sidecar.bytes.AsSpan().SequenceEqual(files[path]))
                    throw new InvalidDataException($"Missing or stale imported Unity GA data '{path}'.");
                return sidecar;
            }

            private static bool Matches(string path, byte[] expected) => File.Exists(path) &&
                File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);

            private static byte[] SourceBytes(string value) => Utf8.GetBytes(value.Replace("\r\n", "\n").Replace('\r', '\n'));
        }
    }
}
