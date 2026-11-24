using System.Text;
using System.Text.Json;

namespace SKECS.GA;

/// <summary>Packages the shader and declared layout for later import by Unity.</summary>
public static class GaUnityComputePackager
{
    public static GaArtifactBundle Create(GaProgram program, string simulationContractId,
        int workgroupX = GaSlangModule.ThreadsPerGroup, string? unityVersion = null)
    {
        GaArtifactManifest.ValidateText(simulationContractId, "simulation contract ID");
        if (unityVersion is not null)
            GaArtifactManifest.ValidateText(unityVersion, "Unity Editor version");
        var module = GaUnityComputeEmitter.Emit(program, workgroupX);
        string importer = unityVersion is null ? "Unity Compute Shader importer" : $"Unity {unityVersion} Compute Shader importer";
        var toolchain = new GaToolchain("none", importer, GaArtifactFormat.UnityCompute, GaUnityComputeAbi.TargetProfile,
            [GaUnityComputeAbi.CodegenOption], [], []);
        GaResourceBinding[] bindings =
        [
            Binding(GaUnityComputeAbi.InputBuffer, 0, GaResourceKind.StorageBuffer, GaResourceAccess.Read),
            Binding(GaUnityComputeAbi.OutputBuffer, 1, GaResourceKind.StorageBuffer, GaResourceAccess.ReadWrite),
            Binding(GaUnityComputeAbi.FaultBuffer, 2, GaResourceKind.StorageBuffer, GaResourceAccess.ReadWrite),
            Binding(GaUnityComputeAbi.InputWords, 3, GaResourceKind.UniformBuffer, GaResourceAccess.Read),
            Binding(GaUnityComputeAbi.OutputWords, 4, GaResourceKind.UniformBuffer, GaResourceAccess.Read),
            Binding(GaUnityComputeAbi.FaultWords, 5, GaResourceKind.UniformBuffer, GaResourceAccess.Read)
        ];
        var usage = bindings.Select(binding => new GaResourceUsage(binding.LogicalResourceId, true)).ToArray();
        return GaShaderBundle.Create(program, simulationContractId, toolchain, GaUnityComputeAbi.MainKernel, workgroupX,
            bindings, usage, module.FaultSources, Encoding.UTF8.GetBytes(module.Source),
            WriteLayout(bindings, Math.Max(1, program.WordCount), workgroupX));
    }

    public static async Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId,
        string outputDirectory, string artifactName, int workgroupX = GaSlangModule.ThreadsPerGroup,
        string? unityVersion = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        GaShaderBundle.ValidateArtifactName(artifactName);
        var bundle = Create(program, simulationContractId, workgroupX, unityVersion);
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string stem = Path.Combine(directory, artifactName);
        await File.WriteAllBytesAsync(stem + ".compute", bundle.Artifact.ToArray()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(stem + ".layout", bundle.Reflection.ToArray()).ConfigureAwait(false);
        await GaShaderBundle.WriteMetadata(stem, bundle).ConfigureAwait(false);
        return bundle;
    }

    private static GaResourceBinding Binding(string name, int index, GaResourceKind kind, GaResourceAccess access) =>
        new(name, GaBindingNamespace.UnityComputeProperty, 0, index, 1, kind, access,
            new GaResourceLayout(GaUnityComputeAbi.WordStride, GaUnityComputeAbi.WordStride,
                kind == GaResourceKind.StorageBuffer ? GaUnityComputeAbi.WordStride : 0, []));

    // Unity cannot reflect the shader before importing it. Record the generator's layout as the import contract.
    private static byte[] WriteLayout(GaResourceBinding[] bindings, int wordsPerRow, int workgroupX)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("mainKernel", GaUnityComputeAbi.MainKernel);
            writer.WriteString("probeKernel", GaUnityComputeAbi.ProbeKernel);
            writer.WriteNumber("workgroupX", workgroupX);
            writer.WriteNumber("wordsPerRow", wordsPerRow);
            writer.WriteNumber("faultWordsPerRow", GaSlangModule.FaultStride);
            writer.WriteStartArray("properties");
            foreach (var binding in bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("name", binding.LogicalResourceId);
                writer.WriteString("kind", binding.Kind.ToString());
                writer.WriteString("access", binding.Access.ToString());
                writer.WriteNumber("elementStride", binding.Layout!.ElementStride);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
