using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.GA;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("""
        Usage: dotnet Ga.Build.dll <request.json>

        Required JSON properties
          sources                 C# paths relative to sourceRoot
          systemId                SecsSystem SystemId, or its global:: qualified type
          simulationContractId    Consumer simulation contract
          outputDirectory         Request-relative path for generated packages
          artifactName            Safe file stem for generated artifacts

        Metadata references
          references              Request-relative or absolute assembly paths
          referenceDirectories    Request-relative or absolute directories of reference assemblies

        Optional properties
          sourceRoot (.), adapterClass (GaCpuAdapter), policy (auto), slangAdapter
          target (vulkan, d3d12, d3d11, opengl, or webgpu)
          dxcDirectory (d3d12), fxcDirectory (d3d11), spirvCrossExecutable (opengl)
          OpenGL also uses the pinned spirv-opt.exe in the SPIRV-Cross directory.
          slangCompilerExecutable (webgpu) uses the pinned slangc.exe and adjacent slang.dll.
          workgroupX (64)          One-dimensional GPU workgroup width
          checkOverflow (false), languageVersion (12.0), defines, nullable (disable)
          allowUnsafe (false), assemblyName (GaConsumer), outputKind (dynamicallyLinkedLibrary)

        Policies: cpuOnly, auto, requireGpu
        Stdout reports the immutable output directory once the CPU adapter and GPU row codec compile.
        If requireGpu is requested and GPU compilation fails, the report is unsuccessful and the command exits 1.
        """);
    return 0;
}
if (args is not [var requestPath])
{
    Console.Error.WriteLine("Usage: dotnet Ga.Build.dll <request.json>");
    return 2;
}

GaBuildRequest request;
string requestDirectory;
try
{
    requestPath = Path.GetFullPath(requestPath);
    requestDirectory = Path.GetDirectoryName(requestPath)!;
    request = JsonSerializer.Deserialize<GaBuildRequest>(File.ReadAllText(requestPath), GaBuildRequest.JsonOptions)
        ?? throw new ArgumentException("The GA build request is empty.");
    request.Validate();
}
catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(failure.Message);
    return 2;
}

try
{
    var compilation = request.CreateCompilation(requestDirectory);
    var inputErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (inputErrors.Length != 0)
    {
        PrintDiagnostics(inputErrors);
        return 1;
    }
    var method = request.FindExecute(compilation);
    var result = GaCompiler.Compile(compilation, method);
    PrintDiagnostics(result.Diagnostics);
    if (result.Program is not { } program)
        return 1;

    string cpuSource = GaCpuAdapterEmitter.Emit(program, request.AdapterClass).ReplaceLineEndings("\n");
    string codecSource = GaRowCodecEmitter.Emit(program, request.AdapterClass + "RowCodec").ReplaceLineEndings("\n");
    var parseOptions = (CSharpParseOptions)method.DeclaringSyntaxReferences[0].SyntaxTree.Options;
    var adapterTree = CSharpSyntaxTree.ParseText(cpuSource, parseOptions, path: "ga-adapter.g.cs", encoding: Encoding.UTF8);
    var codecTree = CSharpSyntaxTree.ParseText(codecSource, parseOptions, path: "ga-row-codec.g.cs", encoding: Encoding.UTF8);
    var adapterErrors = compilation.AddSyntaxTrees(adapterTree, codecTree).GetDiagnostics()
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (adapterErrors.Length != 0)
    {
        PrintDiagnostics(adapterErrors);
        return 1;
    }

    var output = await Publish(request, requestDirectory, program, cpuSource, codecSource);
    Console.WriteLine(JsonSerializer.Serialize(output, GaBuildRequest.JsonOptions));
    return output.Report.Succeeded ? 0 : 1;
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException
    or NotSupportedException or BadImageFormatException)
{
    Console.Error.WriteLine(failure.Message);
    return 1;
}

static void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
        Console.Error.WriteLine(diagnostic);
}

static async Task<GaBuildOutput> Publish(GaBuildRequest request, string requestDirectory, GaProgram program,
    string cpuSource, string codecSource)
{
    const string adapterName = "ga-adapter.g.cs";
    const string codecName = "ga-row-codec.g.cs";
    const string reportName = "build-report.json";
    string outputRoot = Path.GetFullPath(request.OutputDirectory, requestDirectory);
    Directory.CreateDirectory(outputRoot);
    string staging = Path.Combine(outputRoot, ".ga-build-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(staging);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(staging, adapterName), cpuSource);
        await File.WriteAllTextAsync(Path.Combine(staging, codecName), codecSource);
        var gpu = await BuildGpu(request, requestDirectory, program, staging);
        if (gpu.Status == GpuBuildStatus.Unavailable)
        {
            foreach (string shaderFile in Directory.EnumerateFiles(staging))
                if (Path.GetFileName(shaderFile) is not (adapterName or codecName))
                    File.Delete(shaderFile);
        }

        var files = Directory.EnumerateFiles(staging).Order(StringComparer.Ordinal).Select(path =>
            new BuildFile(Path.GetFileName(path), GaArtifactManifest.ComputeDigest(File.ReadAllBytes(path)))).ToArray();
        bool succeeded = request.Policy != GaPolicy.RequireGpu || gpu.Status == GpuBuildStatus.Emitted;
        var options = new CSharpBuildOptions(request.AssemblyName, request.LanguageVersion,
            request.Defines.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            request.Nullable, request.CheckOverflow, request.AllowUnsafe, request.OutputKind);
        var report = new GaBuildReport(succeeded, request.Policy, program.SystemId, request.SimulationContractId,
            program.SourceDigest, options, adapterName, codecName, gpu, files);
        byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, GaBuildRequest.JsonOptions);
        string contentId = GaArtifactManifest.ComputeDigest(reportBytes);
        string destination = Path.Combine(outputRoot, request.ArtifactName + "-" + contentId);
        await File.WriteAllBytesAsync(Path.Combine(staging, reportName), reportBytes);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(staging, destination);
                break;
            }
            catch (Exception failure) when (Directory.Exists(destination) &&
                failure is IOException or UnauthorizedAccessException)
            {
                var reportFile = new BuildFile(reportName, contentId);
                var existing = Directory.EnumerateFileSystemEntries(destination).ToArray();
                if (existing.Length != files.Length + 1 || !files.Append(reportFile).All(file =>
                        File.Exists(Path.Combine(destination, file.Path)) &&
                        GaArtifactManifest.ComputeDigest(File.ReadAllBytes(Path.Combine(destination, file.Path))) == file.Digest))
                    throw new IOException($"Existing GA output directory '{destination}' failed its content checks.", failure);
                break;
            }
            catch (Exception failure) when (OperatingSystem.IsWindows() && attempt < 6 && Directory.Exists(staging) &&
                (failure is UnauthorizedAccessException || failure is IOException &&
                    (failure.HResult & 0xffff) is 5 or 32 or 33))
            {
                // A just-produced artifact can still be held by a Windows file scanner.
                await Task.Delay(25 << attempt);
            }
        }
        return new GaBuildOutput(destination, contentId, report);
    }
    finally
    {
        if (Directory.Exists(staging))
        {
            foreach (string generatedFile in Directory.EnumerateFiles(staging))
                File.Delete(generatedFile);
            Directory.Delete(staging);
        }
    }
}

static async Task<GpuBuildResult> BuildGpu(
    GaBuildRequest request, string requestDirectory, GaProgram program, string staging)
{
    if (request.Policy == GaPolicy.CpuOnly)
        return new GpuBuildResult(GpuBuildStatus.NotRequested);
    bool webGpu = request.Target == "webgpu";
    string? executable = webGpu ? request.SlangCompilerExecutable : request.SlangAdapter;
    string? slang = executable is null ? null : Path.GetFullPath(executable, requestDirectory);
    if (slang is null || !File.Exists(slang))
        return webGpu
            ? new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "SlangCompilerMissing",
                Detail: "Supply the pinned slangc.exe to build a WebGPU package.")
            : new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "SlangAdapterMissing",
                Detail: "Supply the pinned Slang API adapter executable to build a GPU package.");
    if (webGpu && !File.Exists(Path.Combine(Path.GetDirectoryName(slang)!, "slang.dll")))
        return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "SlangLibraryMissing",
            Detail: "Supply the pinned slang.dll beside slangc.exe.");
    if (request.Target == "d3d12" && (request.DxcDirectory is null ||
        !File.Exists(Path.Combine(Path.GetFullPath(request.DxcDirectory, requestDirectory), "dxcompiler.dll"))))
        return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "DxcMissing",
            Detail: "Supply the directory containing the pinned dxcompiler.dll to build a D3D12 package.");
    if (request.Target == "d3d11" && (request.FxcDirectory is null ||
        !File.Exists(Path.Combine(Path.GetFullPath(request.FxcDirectory, requestDirectory), "d3dcompiler_47.dll"))))
        return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "FxcMissing",
            Detail: "Supply the directory containing the pinned d3dcompiler_47.dll to build a D3D11 package.");
    string? cross = null;
    if (request.Target == "opengl")
    {
        cross = request.SpirvCrossExecutable is null ? null : Path.GetFullPath(request.SpirvCrossExecutable, requestDirectory);
        if (cross is null || !File.Exists(cross))
            return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "SpirvCrossMissing",
                Detail: "Supply the pinned SPIRV-Cross executable to build an OpenGL package.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(cross)!, "spirv-opt.exe")))
            return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "SpirvOptMissing",
                Detail: "Supply the pinned spirv-opt.exe beside the SPIRV-Cross executable.");
    }

    try
    {
        var bundle = request.Target switch
        {
            "webgpu" => await GaWebGpuPackager.Build(program, request.SimulationContractId, slang,
                staging, request.ArtifactName, request.WorkgroupX),
            "opengl" => await GaOpenGlPackager.Build(program, request.SimulationContractId, slang, cross!,
                staging, request.ArtifactName, request.WorkgroupX),
            "d3d11" => await GaD3D11Packager.Build(program, request.SimulationContractId, slang,
                Path.GetFullPath(request.FxcDirectory!, requestDirectory), staging, request.ArtifactName, request.WorkgroupX),
            "d3d12" => await GaD3D12Packager.Build(program, request.SimulationContractId, slang,
                Path.GetFullPath(request.DxcDirectory!, requestDirectory), staging, request.ArtifactName, request.WorkgroupX),
            _ => await GaVulkanPackager.Build(program, request.SimulationContractId, slang, staging, request.ArtifactName,
                request.WorkgroupX)
        };
        return new GpuBuildResult(GpuBuildStatus.Emitted, bundle.ContentId, request.ArtifactName + ".manifest.json");
    }
    catch (NotSupportedException unsupported)
    {
        return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "GpuLoweringUnavailable", Detail: unsupported.Message);
    }
    catch (Exception failure) when (failure is InvalidDataException or Win32Exception or IOException
        or UnauthorizedAccessException or DecoderFallbackException)
    {
        return new GpuBuildResult(GpuBuildStatus.Unavailable, Code: "GpuCompilationFailed",
            Detail: failure.Message.Replace(staging, "<output>", StringComparison.OrdinalIgnoreCase));
    }
}

internal enum GpuBuildStatus { NotRequested, Emitted, Unavailable }
internal sealed record GpuBuildResult(
    GpuBuildStatus Status, string? ContentId = null, string? Manifest = null, string? Code = null, string? Detail = null);
internal sealed record BuildFile(string Path, string Digest);
internal sealed record CSharpBuildOptions(string AssemblyName, string LanguageVersion, IReadOnlyList<string> Defines,
    NullableContextOptions Nullable, bool CheckOverflow, bool AllowUnsafe, OutputKind OutputKind);
internal sealed record GaBuildReport(bool Succeeded, GaPolicy Policy, string SystemId, string SimulationContractId,
    string SourceDigest, CSharpBuildOptions CSharpOptions, string CpuAdapter, string RowCodec,
    GpuBuildResult Gpu, IReadOnlyList<BuildFile> Files);
internal sealed record GaBuildOutput(string OutputDirectory, string ContentId, GaBuildReport Report);
