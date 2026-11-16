using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SKECS.GA;

public static class GaVulkanPackager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string outputDirectory, string artifactName) =>
        Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName, GaSlangModule.ThreadsPerGroup);

    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string outputDirectory, string artifactName, int workgroupX) =>
        GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName,
            GaArtifactFormat.SpirV, null, workgroupX);
}

public static class GaD3D12Packager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string dxcDirectory, string outputDirectory, string artifactName) =>
        Build(program, simulationContractId, slangAdapterPath, dxcDirectory, outputDirectory, artifactName,
            GaSlangModule.ThreadsPerGroup);

    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string dxcDirectory, string outputDirectory, string artifactName, int workgroupX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dxcDirectory);
        return GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName,
            GaArtifactFormat.Dxil, Path.GetFullPath(dxcDirectory), workgroupX);
    }
}

public static class GaD3D11Packager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string fxcDirectory, string outputDirectory, string artifactName) =>
        Build(program, simulationContractId, slangAdapterPath, fxcDirectory, outputDirectory, artifactName,
            GaSlangModule.ThreadsPerGroup);

    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string fxcDirectory, string outputDirectory, string artifactName, int workgroupX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fxcDirectory);
        return GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName,
            GaArtifactFormat.Dxbc, Path.GetFullPath(fxcDirectory), workgroupX);
    }
}

internal static class GaArtifactPackager
{
    internal static async Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangExecutablePath, string outputDirectory, string artifactName, GaArtifactFormat format,
        string? downstreamPath, int workgroupX, bool embeddedProfile = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(simulationContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slangExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        if (artifactName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException("Use an ASCII artifact filename without an extension.", nameof(artifactName));

        // DXBC and DXIL use the same explicit storage registers.
        var module = format switch
        {
            GaArtifactFormat.SpirV or GaArtifactFormat.Glsl => GaSlangEmitter.EmitVulkan(program, workgroupX),
            GaArtifactFormat.Wgsl => GaSlangEmitter.EmitWebGpu(program, workgroupX),
            _ => GaSlangEmitter.EmitD3D12(program, workgroupX)
        };
        string? fxcPath = format == GaArtifactFormat.Dxbc ? Path.Combine(downstreamPath!, "d3dcompiler_47.dll") : null;
        string? optimizerPath = format == GaArtifactFormat.Glsl
            ? Path.Combine(Path.GetDirectoryName(downstreamPath!)!, "spirv-opt.exe")
            : null;
        // Keep the verified downstream tools open for reading until the build finishes.
        using var fxcFile = fxcPath is null ? null : File.OpenRead(fxcPath);
        using var crossFile = format == GaArtifactFormat.Glsl ? File.OpenRead(downstreamPath!) : null;
        using var optimizerFile = optimizerPath is null ? null : File.OpenRead(optimizerPath);
        using var slangCompiler = format == GaArtifactFormat.Wgsl ? File.OpenRead(slangExecutablePath) : null;
        using var slangLibrary = format == GaArtifactFormat.Wgsl
            ? File.OpenRead(Path.Combine(Path.GetDirectoryName(slangExecutablePath)!, "slang.dll"))
            : null;
        var toolchain = format switch
        {
            GaArtifactFormat.SpirV => VulkanToolchain(),
            GaArtifactFormat.Dxil => D3D12Toolchain(downstreamPath!),
            GaArtifactFormat.Dxbc => D3D11Toolchain(fxcPath!, fxcFile!),
            GaArtifactFormat.Glsl => GaOpenGlCompiler.ReadToolchain(crossFile!, optimizerFile!, embeddedProfile),
            GaArtifactFormat.Wgsl => GaWebGpuCompiler.ReadToolchain(slangCompiler!, slangLibrary!),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string stem = Path.Combine(directory, artifactName);
        string sourcePath = stem + ".slang";
        string binaryPath = stem + (format == GaArtifactFormat.SpirV ? ".spv" : "." + format.ToString().ToLowerInvariant());
        string reflectionPath = stem + ".layout";
        await File.WriteAllTextAsync(sourcePath, module.Source).ConfigureAwait(false);
        GaResourceBinding[] bindings;
        GaResourceUsage[] usage;
        byte[] reflection;
        if (format == GaArtifactFormat.Glsl)
        {
            (bindings, usage) = await CompileOpenGl(directory, slangExecutablePath, downstreamPath!, optimizerPath!, sourcePath,
                binaryPath, reflectionPath, workgroupX, embeddedProfile).ConfigureAwait(false);
            reflection = await File.ReadAllBytesAsync(reflectionPath).ConfigureAwait(false);
        }
        else if (format == GaArtifactFormat.Wgsl)
        {
            (bindings, usage) = await GaWebGpuCompiler.Compile(slangExecutablePath, sourcePath, binaryPath, reflectionPath,
                workgroupX).ConfigureAwait(false);
            reflection = await File.ReadAllBytesAsync(reflectionPath).ConfigureAwait(false);
        }
        else
        {
            await CompileShader(slangExecutablePath, sourcePath, binaryPath, reflectionPath, format, downstreamPath)
                .ConfigureAwait(false);
            reflection = await File.ReadAllBytesAsync(reflectionPath).ConfigureAwait(false);
            using var reader = new StringReader(Encoding.UTF8.GetString(reflection));
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);
            (bindings, usage) = ParseReflection(lines.ToArray(), toolchain, workgroupX);
        }
        byte[] binary = await File.ReadAllBytesAsync(binaryPath).ConfigureAwait(false);
        byte[] sourceMap = JsonSerializer.SerializeToUtf8Bytes(module.FaultSources.ToArray(),
            GaManifestJsonContext.Default.GaSourceSpanArray);
        var manifest = new GaArtifactManifest(GaArtifactManifest.CurrentSchemaVersion,
            new GaContractIdentity(program.SystemId, simulationContractId, program.SourceDigest,
                GaIrDigest.Program(program), GaIrDigest.Schema(program), GaProgram.NumericVersion,
                GaProgram.SubsetVersion, GaProgram.IrVersion)
            {
                EventCapacity = program.EventCapacity,
                UsesSnapshot = program.UsesSnapshot,
                SnapshotSchema = program.SnapshotLayout?.Schema,
                TickResourceType = program.TickResourceType,
                TickResourceWords = program.TickResourceWords,
                BufferSchema = program.BufferSchema,
                BufferRowWords = program.BufferRowWords,
                UsesCommands = program.UsesCommands,
                ComponentRemovalCount = program.RemovalComponents.Count,
                ComponentAdditionCount = program.AdditionComponents.Count,
                AdditionPayloadWords = program.AdditionPayloadWords,
                EventTypeCount = program.EventTypes.Count,
                EventPayloadWords = program.EventPayloadWords,
                ReductionTypeCount = program.ReductionTypeCount,
                UsesDomainOrder = program.UsesDomainOrder
            },
            toolchain, GaSlangModule.EntryPoint, bindings, usage,
            new GaKernelRequirements(program.UsesFixed ? GaComputeProfiles.WideFixedEmulated : GaComputeProfiles.Portable32,
                [], workgroupX, 1, 1, 0, 0),
            GaArtifactManifest.ComputeDigest(binary), GaArtifactManifest.ComputeDigest(reflection),
            GaArtifactManifest.ComputeDigest(sourceMap));
        var bundle = GaArtifactBundle.Load(manifest, manifest.GetContentId(), binary, reflection, sourceMap);
        await File.WriteAllBytesAsync(stem + ".source-map.json", sourceMap).ConfigureAwait(false);
        await File.WriteAllBytesAsync(stem + ".manifest.json", manifest.GetCanonicalBytes()).ConfigureAwait(false);
        await File.WriteAllTextAsync(stem + ".content-id", bundle.ContentId + "\n").ConfigureAwait(false);
        return bundle;
    }

    private static async Task<(GaResourceBinding[], GaResourceUsage[])> CompileOpenGl(string outputDirectory,
        string slangAdapterPath, string spirvCrossExecutable, string spirvOptimizer, string sourcePath,
        string shaderPath, string reflectionPath, int workgroupX, bool embeddedProfile)
    {
        string staging = Path.Combine(outputDirectory, ".ga-opengl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string spirv = Path.Combine(staging, "kernel.spv");
            string slangLayout = Path.Combine(staging, "slang.layout");
            await CompileShader(slangAdapterPath, sourcePath, spirv, slangLayout, GaArtifactFormat.SpirV, null)
                .ConfigureAwait(false);
            var (bindings, usage) = ParseReflection(await File.ReadAllLinesAsync(slangLayout).ConfigureAwait(false),
                VulkanToolchain(), workgroupX);
            await GaOpenGlCompiler.Translate(spirvCrossExecutable, spirvOptimizer, spirv, staging, shaderPath, reflectionPath,
                bindings, workgroupX, embeddedProfile).ConfigureAwait(false);
            return (bindings.Select(binding => binding with { Namespace = GaBindingNamespace.OpenGlShaderStorage })
                .ToArray(), usage);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(staging))
                File.Delete(file);
            Directory.Delete(staging);
        }
    }

    private static GaToolchain VulkanToolchain() => new("2025.6.1", "Slang direct SPIR-V emitter", GaArtifactFormat.SpirV,
        "spirv_1_3", ["GenerateSpirvDirectly", "VulkanUseEntryPointName"], [], []);

    private static GaToolchain D3D12Toolchain(string dxcDirectory)
    {
        const string pinnedDXC = "1.8.0.4806 (75a029d95)";
        string library = Path.Combine(dxcDirectory, "dxcompiler.dll");
        var version = FileVersionInfo.GetVersionInfo(library).ProductVersion;
        if (version != pinnedDXC)
            throw new InvalidDataException($"Expected Microsoft DXC {pinnedDXC}; found '{version}'.");
        return new GaToolchain("2025.6.1", "Microsoft DXC " + pinnedDXC, GaArtifactFormat.Dxil, "sm_6_0",
            ["Slang downstream DXC"], [], []);
    }

    private static GaToolchain D3D11Toolchain(string library, FileStream compiler)
    {
        const string pinnedFxc = "10.0.26100.7705";
        const string pinnedSha256 = "2B23EFDC8B45ADC4B79C1C0B940A906336628CD35ED80DEFD489D70D22FF6D74";
        var info = FileVersionInfo.GetVersionInfo(library);
        string version = FormattableString.Invariant($"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}");
        string sha256 = Convert.ToHexString(SHA256.HashData(compiler));
        if (version != pinnedFxc || sha256 != pinnedSha256)
            throw new InvalidDataException($"Expected Microsoft FXC {pinnedFxc} with SHA-256 {pinnedSha256}; found {version} with SHA-256 {sha256}.");
        return new GaToolchain("2025.6.1", $"Microsoft FXC {version} sha256:{sha256.ToLowerInvariant()}",
            GaArtifactFormat.Dxbc, "sm_5_0", ["Slang HLSL output", "D3DCOMPILE_SKIP_OPTIMIZATION"], [], []);
    }

    private static Task CompileShader(string executable, string source, string binary, string reflection,
        GaArtifactFormat format, string? compilerDirectory)
    {
        string command = format switch
        {
            GaArtifactFormat.Dxil => "compile-dxil",
            GaArtifactFormat.Dxbc => "compile-dxbc",
            _ => "compile"
        };
        List<string> arguments = [command, source, GaSlangModule.EntryPoint, binary, reflection];
        if (compilerDirectory != null)
            arguments.Add(compilerDirectory);
        return RunCompiler(executable, arguments, "Slang");
    }

    internal static async Task RunCompiler(string executable, IReadOnlyList<string> arguments, string compiler)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Cannot start the {compiler} compilation adapter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"{compiler} compilation failed ({process.ExitCode}).\n{await output.ConfigureAwait(false)}{await errors.ConfigureAwait(false)}");
    }

    private static (GaResourceBinding[], GaResourceUsage[]) ParseReflection(string[] lines, GaToolchain toolchain, int workgroupX)
    {
        if (lines.Length != 7 ||
            lines[0] != "slang\t2025.6.1" ||
            lines[1] != $"target\t{toolchain.Format.ToString().ToLowerInvariant()}\t{toolchain.TargetProfile}" ||
            lines[2] != "entry\t" + GaSlangModule.EntryPoint ||
            lines[3] != FormattableString.Invariant($"workgroup\t{workgroupX}\t1\t1"))
            throw new InvalidDataException("Unexpected Slang toolchain, entry point or workgroup reflection.");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gaInput"] = "r",
            ["gaOutput"] = "rw",
            ["gaFaults"] = "rw"
        };
        var bindings = new List<GaResourceBinding>(3);
        var usage = new List<GaResourceUsage>(3);
        foreach (string line in lines[4..])
        {
            string[] fields = line.Split('\t');
            if (fields is not ["binding", var logical, var set, var index, "1", "storage", var access, "used", "4", "4"]
                || !expected.Remove(logical, out var expectedAccess) || access != expectedAccess
                || !int.TryParse(set, NumberStyles.None, CultureInfo.InvariantCulture, out int space)
                || !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int binding)
                || toolchain.Format == GaArtifactFormat.Dxbc && (space != 0 || binding >= (access == "r" ? 128 : 8)))
                throw new InvalidDataException("Unexpected or missing storage layout/usage in the Slang entry point.");
            var bindingNamespace = toolchain.Format != GaArtifactFormat.SpirV
                ? access == "r" ? GaBindingNamespace.D3DResource : GaBindingNamespace.D3DUnorderedAccess
                : GaBindingNamespace.VulkanDescriptor;
            bindings.Add(new GaResourceBinding(logical, bindingNamespace, space, binding, 1,
                GaResourceKind.StorageBuffer, access == "r" ? GaResourceAccess.Read : GaResourceAccess.ReadWrite,
                new GaResourceLayout(4, 4, 4, [])));
            usage.Add(new GaResourceUsage(logical, true));
        }
        return (bindings.ToArray(), usage.ToArray());
    }
}
