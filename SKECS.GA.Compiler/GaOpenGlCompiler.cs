using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SKECS.GA;

public static class GaOpenGlPackager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId, string slangAdapterPath,
        string spirvCrossExecutable, string outputDirectory, string artifactName) =>
        Build(program, simulationContractId, slangAdapterPath, spirvCrossExecutable, outputDirectory, artifactName,
            GaSlangModule.ThreadsPerGroup);

    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId, string slangAdapterPath,
        string spirvCrossExecutable, string outputDirectory, string artifactName, int workgroupX)
        => Build(program, simulationContractId, slangAdapterPath, spirvCrossExecutable, outputDirectory, artifactName,
            workgroupX, embeddedProfile: false);

    public static Task<GaArtifactBundle> BuildEs(GaProgram program, string simulationContractId, string slangAdapterPath,
        string spirvCrossExecutable, string outputDirectory, string artifactName,
        int workgroupX = GaSlangModule.ThreadsPerGroup)
        => Build(program, simulationContractId, slangAdapterPath, spirvCrossExecutable, outputDirectory, artifactName,
            workgroupX, embeddedProfile: true);

    private static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId, string slangAdapterPath,
        string spirvCrossExecutable, string outputDirectory, string artifactName, int workgroupX, bool embeddedProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spirvCrossExecutable);
        return GaArtifactPackager.Build(program, simulationContractId, slangAdapterPath, outputDirectory, artifactName,
            GaArtifactFormat.Glsl, Path.GetFullPath(spirvCrossExecutable), workgroupX, embeddedProfile);
    }
}

internal static class GaOpenGlCompiler
{
    private const string CrossRevision = "vulkan-sdk-1.4.304.0-40-g2c32b6bf";
    private const string CrossSha256 = "cc19353f15b3db467d58d5e0a97add7a6797fbc52313c57d38b0f77bfa2f90a1";
    private const string OptimizerVersion = "v2025.1 v2025.1.rc1-0-gf289d047";
    private const string OptimizerSha256 = "e8ced090e643ab277296039e61c19de39c23535bdebd31109e8e7db23a86c303";
    private const string SpirvTarget = "--target-env=vulkan1.1";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    // Keep the pinned optimizer's -O sequence except if-conversion. Its vector selects became
    // integer mix calls that ANGLE could link but could not compile when first dispatching on D3D11.
    private static readonly string[] EsOptimizationPasses =
    [
        "--wrap-opkill", "--eliminate-dead-branches", "--merge-return", "--inline-entry-points-exhaustive",
        "--eliminate-dead-functions", "--eliminate-dead-code-aggressive", "--private-to-local",
        "--eliminate-local-single-block", "--eliminate-local-single-store", "--eliminate-dead-code-aggressive",
        "--scalar-replacement=100", "--convert-local-access-chains", "--eliminate-local-single-block",
        "--eliminate-local-single-store", "--eliminate-dead-code-aggressive", "--ssa-rewrite",
        "--eliminate-dead-code-aggressive", "--ccp", "--eliminate-dead-code-aggressive", "--loop-unroll",
        "--eliminate-dead-branches", "--redundancy-elimination", "--combine-access-chains",
        "--simplify-instructions", "--scalar-replacement=100", "--convert-local-access-chains",
        "--eliminate-local-single-block", "--eliminate-local-single-store", "--eliminate-dead-code-aggressive",
        "--ssa-rewrite", "--eliminate-dead-code-aggressive", "--vector-dce", "--eliminate-dead-inserts",
        "--eliminate-dead-branches", "--simplify-instructions", "--copy-propagate-arrays", "--reduce-load-size",
        "--eliminate-dead-code-aggressive", "--merge-blocks", "--redundancy-elimination",
        "--eliminate-dead-branches", "--merge-blocks", "--simplify-instructions"
    ];

    internal static GaToolchain ReadToolchain(FileStream cross, FileStream optimizer, bool embeddedProfile)
    {
        ValidateToolHash(cross, $"SPIRV-Cross {CrossRevision}", CrossSha256);
        ValidateToolHash(optimizer, $"SPIRV-Tools {OptimizerVersion}", OptimizerSha256);
        string downstream = $"SPIRV-Tools {OptimizerVersion} sha256:{OptimizerSha256}, SPIRV-Cross {CrossRevision} sha256:{CrossSha256}";
        var (version, esFlag) = GetTranslationOptions(embeddedProfile);
        return new GaToolchain("2025.6.1", downstream, GaArtifactFormat.Glsl,
            embeddedProfile ? "glsl_es_310" : "glsl_430",
            ["GenerateSpirvDirectly", "VulkanUseEntryPointName", SpirvTarget,
                embeddedProfile ? "-O without --if-conversion" : "-O", $"--version {version}", esFlag],
            [], []);
    }

    private static (string Version, string EsFlag) GetTranslationOptions(bool embeddedProfile) =>
        embeddedProfile ? ("310", "--es") : ("430", "--no-es");

    private static void ValidateToolHash(FileStream executable, string name, string expected)
    {
        string digest = Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant();
        if (digest != expected)
            throw new InvalidDataException($"Expected {name} with SHA-256 {expected}; found {digest}.");
    }

    internal static async Task Translate(string cross, string optimizer, string spirvPath, string outputDirectory,
        string shaderPath, string reflectionPath, GaResourceBinding[] bindings, int workgroupX, bool embeddedProfile)
    {
        string optimized = Path.Combine(outputDirectory, "optimized.spv");
        string jsonPath = Path.Combine(outputDirectory, "cross-layout.json");
        string glslPath = Path.Combine(outputDirectory, "cross.comp");
        // The spatial conformance loop diverged on real OpenGL when translated without this step.
        string[] passes = embeddedProfile ? EsOptimizationPasses : ["-O"];
        await GaArtifactPackager.RunCompiler(optimizer, [SpirvTarget, .. passes, spirvPath, "-o", optimized],
            "SPIRV-Tools").ConfigureAwait(false);
        await GaArtifactPackager.RunCompiler(cross, [optimized, "--reflect", "--output", jsonPath], "SPIRV-Cross")
            .ConfigureAwait(false);
        string layout = CanonicalText(await File.ReadAllTextAsync(jsonPath, Utf8).ConfigureAwait(false));
        ValidateReflection(layout, bindings, workgroupX);

        var (version, esFlag) = GetTranslationOptions(embeddedProfile);
        await GaArtifactPackager.RunCompiler(cross,
            [optimized, "--version", version, esFlag, "--output", glslPath], "SPIRV-Cross").ConfigureAwait(false);
        string shader = CanonicalText(await File.ReadAllTextAsync(glslPath, Utf8).ConfigureAwait(false));
        string versionDirective = embeddedProfile ? $"#version {version} es\n" : $"#version {version}\n";
        if (!shader.StartsWith(versionDirective, StringComparison.Ordinal) ||
            shader.Split('\n').Any(line => line.TrimStart().StartsWith("#extension", StringComparison.Ordinal)))
            throw new InvalidDataException(embeddedProfile
                ? "SPIRV-Cross must produce GLSL ES 310 compute source without extra extensions."
                : "SPIRV-Cross must produce GLSL 430 compute source without extra extensions.");
        await File.WriteAllTextAsync(shaderPath, shader, Utf8).ConfigureAwait(false);
        await File.WriteAllTextAsync(reflectionPath, layout, Utf8).ConfigureAwait(false);
    }

    private static string CanonicalText(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static void ValidateReflection(string layout, GaResourceBinding[] bindings, int workgroupX)
    {
        try
        {
            using var document = JsonDocument.Parse(layout);
            var root = document.RootElement;
            var entryPoints = root.GetProperty("entryPoints");
            if (entryPoints.GetArrayLength() != 1 ||
                root.EnumerateObject().Any(field => field.Name is not ("entryPoints" or "types" or "ssbos")))
                throw new InvalidDataException("SPIRV-Cross must expose one compute entry and only shader storage blocks.");

            var entry = entryPoints[0];
            var group = entry.GetProperty("workgroup_size").EnumerateArray().Select(axis => axis.GetInt32());
            var specialized = entry.GetProperty("workgroup_size_is_spec_constant_id").EnumerateArray()
                .Select(axis => axis.GetBoolean());
            if (entry.GetProperty("name").GetString() != GaSlangModule.EntryPoint ||
                entry.GetProperty("mode").GetString() != "comp" || !group.SequenceEqual([workgroupX, 1, 1]) ||
                !specialized.SequenceEqual([false, false, false]))
                throw new InvalidDataException("SPIRV-Cross entry point or workgroup differs from Slang reflection.");

            var expected = bindings.ToDictionary(binding => binding.LogicalResourceId, StringComparer.Ordinal);
            var types = root.GetProperty("types");
            foreach (var block in root.GetProperty("ssbos").EnumerateArray())
            {
                string name = block.GetProperty("name").GetString() ?? "";
                bool readOnly = block.TryGetProperty("readonly", out var read) && read.GetBoolean();
                bool writeOnly = block.TryGetProperty("writeonly", out var write) && write.GetBoolean();
                if (!expected.Remove(name, out var binding) || binding.Space != 0 || binding.Count != 1 ||
                    block.GetProperty("set").GetInt32() != 0 || block.GetProperty("binding").GetInt32() != binding.Index ||
                    block.GetProperty("block_size").GetInt32() != 0 || writeOnly ||
                    readOnly != (binding.Access == GaResourceAccess.Read))
                    throw new InvalidDataException("SPIRV-Cross storage bindings differ from Slang reflection.");

                string type = block.GetProperty("type").GetString() ?? "";
                var members = types.GetProperty(type).GetProperty("members");
                if (members.GetArrayLength() != 1)
                    throw new InvalidDataException("Expected a single runtime array in each OpenGL storage block.");
                var member = members[0];
                if (member.GetProperty("type").GetString() != "uint" || member.GetProperty("offset").GetInt32() != 0 ||
                    member.GetProperty("array_stride").GetInt32() != binding.Layout?.ElementStride ||
                    !member.GetProperty("array").EnumerateArray().Select(size => size.GetInt32()).SequenceEqual([0]) ||
                    !member.GetProperty("array_size_is_literal").EnumerateArray().Select(size => size.GetBoolean())
                        .SequenceEqual([true]))
                    throw new InvalidDataException("SPIRV-Cross storage layout differs from Slang uint-array layout.");
            }
            if (expected.Count != 0)
                throw new InvalidDataException("SPIRV-Cross reflection omits required storage bindings.");
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Invalid SPIRV-Cross reflection.", failure);
        }
    }
}
