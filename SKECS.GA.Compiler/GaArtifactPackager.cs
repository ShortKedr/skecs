using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SKECS.GA;

public static class GaVulkanPackager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string outputDirectory, string artifactName) =>
        GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName, null);
}

public static class GaD3D12Packager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string dxcDirectory, string outputDirectory, string artifactName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dxcDirectory);
        return GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName,
            Path.GetFullPath(dxcDirectory));
    }
}

internal static class GaArtifactPackager
{
    internal static async Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string slangAdapterPath, string outputDirectory, string artifactName, string? dxcDirectory)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(simulationContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slangAdapterPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        if (artifactName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException("Use an ASCII artifact filename without an extension.", nameof(artifactName));

        bool d3d12 = dxcDirectory != null;
        var module = d3d12 ? GaSlangEmitter.EmitD3D12(program) : GaSlangEmitter.EmitVulkan(program);
        var toolchain = d3d12 ? D3D12Toolchain(dxcDirectory!) :
            new GaToolchain("2025.6.1", "Slang direct SPIR-V emitter", GaArtifactFormat.SpirV, "spirv_1_3",
                ["GenerateSpirvDirectly", "VulkanUseEntryPointName"], [], []);
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string stem = Path.Combine(directory, artifactName);
        string sourcePath = stem + ".slang";
        string binaryPath = stem + (d3d12 ? ".dxil" : ".spv");
        string reflectionPath = stem + ".layout";
        await File.WriteAllTextAsync(sourcePath, module.Source).ConfigureAwait(false);
        await CompileShader(slangAdapterPath, sourcePath, binaryPath, reflectionPath, dxcDirectory).ConfigureAwait(false);
        byte[] binary = await File.ReadAllBytesAsync(binaryPath).ConfigureAwait(false);
        byte[] reflection = await File.ReadAllBytesAsync(reflectionPath).ConfigureAwait(false);
        using var reflectionReader = new StringReader(Encoding.UTF8.GetString(reflection));
        var reflectionLines = new List<string>();
        while (reflectionReader.ReadLine() is { } line)
            reflectionLines.Add(line);
        var (bindings, usage) = ParseReflection(reflectionLines.ToArray(), d3d12);
        byte[] sourceMap = JsonSerializer.SerializeToUtf8Bytes(module.FaultSources.ToArray(),
            GaManifestJsonContext.Default.GaSourceSpanArray);
        var manifest = new GaArtifactManifest(GaArtifactManifest.CurrentSchemaVersion,
            new GaContractIdentity(program.SystemId, simulationContractId, program.SourceDigest,
                GaIrDigest.Program(program), GaIrDigest.Schema(program), GaProgram.NumericVersion,
                GaProgram.SubsetVersion, GaProgram.IrVersion),
            toolchain, GaSlangModule.EntryPoint, bindings, usage,
            new GaKernelRequirements(program.UsesFixed ? GaComputeProfiles.WideFixedEmulated : GaComputeProfiles.Portable32,
                [], GaSlangModule.ThreadsPerGroup, 1, 1, 0, 0),
            GaArtifactManifest.ComputeDigest(binary), GaArtifactManifest.ComputeDigest(reflection),
            GaArtifactManifest.ComputeDigest(sourceMap));
        var bundle = GaArtifactBundle.Load(manifest, manifest.GetContentId(), binary, reflection, sourceMap);
        await File.WriteAllBytesAsync(stem + ".source-map.json", sourceMap).ConfigureAwait(false);
        await File.WriteAllBytesAsync(stem + ".manifest.json", manifest.GetCanonicalBytes()).ConfigureAwait(false);
        await File.WriteAllTextAsync(stem + ".content-id", bundle.ContentId + "\n").ConfigureAwait(false);
        return bundle;
    }

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

    private static async Task CompileShader(string executable, string source, string binary, string reflection, string? dxcDirectory)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { dxcDirectory != null ? "compile-dxil" : "compile", source, GaSlangModule.EntryPoint, binary, reflection })
            start.ArgumentList.Add(argument);
        if (dxcDirectory != null)
            start.ArgumentList.Add(dxcDirectory);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start the Slang compilation adapter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Slang compilation failed ({process.ExitCode}).\n{await output.ConfigureAwait(false)}{await errors.ConfigureAwait(false)}");
    }

    private static (GaResourceBinding[], GaResourceUsage[]) ParseReflection(string[] lines, bool d3d12)
    {
        if (lines.Length != 7 ||
            lines[0] != "slang\t2025.6.1" ||
            lines[1] != (d3d12 ? "target\tdxil\tsm_6_0" : "target\tspirv\tspirv_1_3") ||
            lines[2] != "entry\t" + GaSlangModule.EntryPoint ||
            lines[3] != FormattableString.Invariant($"workgroup\t{GaSlangModule.ThreadsPerGroup}\t1\t1"))
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
                || !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int binding))
                throw new InvalidDataException("Unexpected or missing storage layout/usage in the Slang entry point.");
            var bindingNamespace = d3d12
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
