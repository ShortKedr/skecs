using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SKECS.GA;

internal sealed class MetalSource
{
    private const string DispatchEntry = "skecs_ga_dispatch";
    private const string CrossDigest = "cc19353f15b3db467d58d5e0a97add7a6797fbc52313c57d38b0f77bfa2f90a1";
    private const string EntryPattern = @"(?m)^kernel\s+void\s+skecs_ga_dispatch\s*\(([^\r\n]*)\)";
    private const string SizeTableName = "spvBufferSizeConstants";
    private static readonly string[] StorageIds = ["gaInput", "gaOutput", "gaFaults"];
    private static readonly GaResourceLayout Word = new(4, 4, 4, []);

    private MetalSource(string artifactName, GaArtifactBundle vulkan, ExportMetadata metadata,
        byte[] source, byte[] reflection, GaResourceBinding[] bindings)
    {
        ArtifactName = artifactName;
        Vulkan = vulkan;
        Platform = metadata.Platform;
        Options = metadata.Options;
        Source = source;
        Reflection = reflection;
        Bindings = bindings;
    }

    internal string ArtifactName { get; }
    internal GaArtifactBundle Vulkan { get; }
    internal string Platform { get; }
    internal string EntryPoint => DispatchEntry;
    internal string[] Options { get; }
    internal byte[] Source { get; }
    internal byte[] Reflection { get; }
    internal GaResourceBinding[] Bindings { get; }

    internal static MetalSource Load(string platform, string bundleDirectory, string sourceDirectory)
    {
        string[] manifests = Directory.GetFiles(bundleDirectory, "*.manifest.json");
        if (manifests.Length != 1)
            throw new InvalidDataException("Expected one GA Vulkan manifest.");
        string path = manifests[0];
        string stem = path[..^".manifest.json".Length];
        string artifactName = Path.GetFileName(stem);
        if (artifactName.Length == 0 || artifactName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("Expected an ASCII artifact name without an extension.");
        string contentId = File.ReadAllText(stem + ".content-id").Trim();
        byte[] rawManifest = File.ReadAllBytes(path);
        if (GaArtifactManifest.ComputeDigest(rawManifest) != contentId)
            throw new InvalidDataException("Vulkan manifest does not match its content ID.");
        var candidate = GaArtifactCandidate.Load(rawManifest, contentId, File.ReadAllBytes(stem + ".spv"),
            File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
        var vulkan = candidate.Bundle ?? throw new InvalidDataException($"Invalid Vulkan bundle: {candidate.Error}");
        vulkan.DecodeSourceMap();
        ValidateVulkanReflection(vulkan);

        byte[] reflection = File.ReadAllBytes(Path.Combine(sourceDirectory, "metal-source.json"));
        var metadata = JsonSerializer.Deserialize<ExportMetadata>(reflection)
            ?? throw new InvalidDataException("Missing Metal export metadata.");
        byte[] source = File.ReadAllBytes(Path.Combine(sourceDirectory, "kernel.metal"));
        string[] options = ["--msl", "--msl-version", "20300", "--msl-decoration-binding",
            "--rename-entry-point", "main", DispatchEntry, "comp"];
        if (platform == "ios")
            options = [.. options, "--msl-ios"];
        if (metadata.Platform != platform || metadata.MetalLanguageVersion != "2.3" ||
            metadata.EntryPoint != DispatchEntry || metadata.VulkanContentId != vulkan.ContentId ||
            metadata.SpirvSha256 != vulkan.Manifest.ArtifactDigest || metadata.SpirvCrossSha256 != CrossDigest ||
            metadata.MetalSourceSha256 != GaArtifactManifest.ComputeDigest(source) ||
            metadata.Options?.SequenceEqual(options) != true)
            throw new InvalidDataException("Metal export does not match the requested platform, source or Vulkan provenance.");
        if (metadata.WorkgroupSize?.SequenceEqual([vulkan.Manifest.Requirements.WorkgroupX, 1, 1]) != true ||
            metadata.StorageSlots?.Count != StorageIds.Length ||
            StorageIds.Where((id, slot) => !metadata.StorageSlots.TryGetValue(id, out int actual) || actual != slot).Any() ||
            metadata.InputLengthInBytesSlot is not (null or 25))
            throw new InvalidDataException("Metal export declares an unsupported compute layout.");

        ValidateSourceAbi(new UTF8Encoding(false, true).GetString(source), metadata.InputLengthInBytesSlot);
        var bindings = vulkan.Manifest.DeclaredBindings
            .Select(binding => binding with { Namespace = GaBindingNamespace.MetalBuffer }).ToList();
        if (metadata.InputLengthInBytesSlot is { } sizeSlot)
            bindings.Add(new GaResourceBinding("gaInputLength", GaBindingNamespace.MetalBuffer, 0, sizeSlot, 1,
                GaResourceKind.UniformBuffer, GaResourceAccess.Read, Word));
        return new MetalSource(artifactName, vulkan, metadata, source, reflection, bindings.ToArray());
    }

    private static void ValidateVulkanReflection(GaArtifactBundle bundle)
    {
        var manifest = bundle.Manifest;
        var requirements = manifest.Requirements;
        if (manifest.Toolchain.Format != GaArtifactFormat.SpirV || manifest.Toolchain.TargetProfile != "spirv_1_3" ||
            manifest.Toolchain.SlangVersion != "2025.6.1" || manifest.EntryPoint != "main" ||
            requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated) ||
            requirements.WorkgroupY != 1 || requirements.WorkgroupZ != 1 ||
            requirements.RequiredFeatures.Count != 0 || requirements.SharedMemoryBytes != 0 ||
            manifest.DeclaredBindings.Count != StorageIds.Length || !manifest.EntryPointUsage.All(usage => usage.Used))
            throw new InvalidDataException("Expected the portable one-dimensional Slang 2025.6.1 Vulkan GA profile.");

        string[] lines = Encoding.UTF8.GetString(bundle.Reflection).Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "slang\t2025.6.1",
            "target\tspirv\tspirv_1_3",
            "entry\tmain",
            FormattableString.Invariant($"workgroup\t{requirements.WorkgroupX}\t1\t1"),
        };
        for (int index = 0; index < StorageIds.Length; index++)
        {
            string id = StorageIds[index];
            var binding = manifest.DeclaredBindings.SingleOrDefault(value => value.LogicalResourceId == id);
            if (binding is null || binding.Namespace != GaBindingNamespace.VulkanDescriptor ||
                binding.Space != 0 || binding.Index != index || binding.Count != 1 ||
                binding.Kind != GaResourceKind.StorageBuffer ||
                binding.Access != (index == 0 ? GaResourceAccess.Read : GaResourceAccess.ReadWrite) ||
                binding.Layout is not { Size: 4, Alignment: 4, ElementStride: 4, Fields.Count: 0 })
                throw new InvalidDataException($"Unexpected Vulkan binding for {id}.");
            string access = index == 0 ? "r" : "rw";
            expected.Add(FormattableString.Invariant($"binding\t{id}\t0\t{index}\t1\tstorage\t{access}\tused\t4\t4"));
        }
        if (lines.Length != expected.Count || !expected.SetEquals(lines))
            throw new InvalidDataException("Vulkan reflection does not describe the declared compute layout.");
    }

    private static void ValidateSourceAbi(string source, int? lengthSlot)
    {
        var signature = Regex.Matches(source, EntryPattern);
        if (signature.Count != 1 || Regex.Matches(source, @"(?m)^\s*kernel\s").Count != 1 ||
            source.Contains('\0') || Regex.IsMatch(source, @"\.GetDimensions\s*\(") ||
            !Regex.IsMatch(source, @"\bstruct\s+StructuredBuffer\s*\{\s*uint\s+_m0\[1\];\s*\};") ||
            !Regex.IsMatch(source, @"\bstruct\s+RWStructuredBuffer\s*\{\s*uint\s+_m0\[1\];\s*\};"))
            throw new InvalidDataException("Metal source is missing the expected entry point or scalar storage layout.");

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "constdeviceStructuredBuffer&gaInput[[buffer(0)]]",
            "deviceRWStructuredBuffer&gaOutput[[buffer(1)]]",
            "deviceRWStructuredBuffer&gaFaults[[buffer(2)]]",
            "uint3gl_GlobalInvocationID[[thread_position_in_grid]]",
        };
        if (lengthSlot == 25)
            expected.Add("constantuint*spvBufferSizeConstants[[buffer(25)]]");
        string[] arguments = signature[0].Groups[1].Value.Split(',')
            .Select(argument => Regex.Replace(argument, @"\s", "")).ToArray();
        if (arguments.Length != expected.Count || !expected.SetEquals(arguments))
            throw new InvalidDataException("Metal source arguments disagree with the exported layout.");

        string body = source[(signature[0].Index + signature[0].Length)..];
        var uses = Regex.Matches(body, $@"\b{SizeTableName}\b");
        if (lengthSlot is null ? uses.Count != 0 : uses.Count == 0 ||
            !uses.All(use => body.AsSpan(use.Index + use.Length).StartsWith("[0]", StringComparison.Ordinal)))
            throw new InvalidDataException("Metal source uses an unsupported input-length table.");
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ExportMetadata(
        [property: JsonRequired] string Platform,
        [property: JsonRequired] string MetalLanguageVersion,
        [property: JsonRequired] string EntryPoint,
        [property: JsonRequired] int[] WorkgroupSize,
        [property: JsonRequired] Dictionary<string, int> StorageSlots,
        [property: JsonRequired] int? InputLengthInBytesSlot,
        [property: JsonRequired] string VulkanContentId,
        [property: JsonRequired] string SpirvSha256,
        [property: JsonRequired] string SpirvCrossSha256,
        [property: JsonRequired] string MetalSourceSha256,
        [property: JsonRequired] string[] Options);
}
