using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKECS.GA;

public sealed record GaContractIdentity(
    string SystemId, string SimulationContractId, string SourceDigest, string IrDigest,
    string ComponentSchemaDigest, int NumericVersion, int SubsetVersion, int IrVersion)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EventCapacity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesSnapshot { get; init; }
}

public enum GaArtifactFormat { SpirV, Dxil, Dxbc, MetalLibrary, Wgsl }

public sealed record GaToolchain(
    string SlangVersion, string DownstreamVersion, [property: JsonRequired] GaArtifactFormat Format, string TargetProfile,
    IReadOnlyList<string> Options, IReadOnlyList<string> Defines, IReadOnlyList<string> Specializations);

public enum GaResourceKind { StorageBuffer, UniformBuffer, PushConstants, Texture, Sampler }
public enum GaResourceAccess { Read, Write, ReadWrite }
public enum GaBindingNamespace
{
    VulkanDescriptor, VulkanPushConstants, D3DConstantBuffer, D3DResource, D3DUnorderedAccess,
    D3DSampler, MetalBuffer, MetalTexture, MetalSampler, WebGpuBinding
}

public sealed record GaLayoutField(
    string Path, [property: JsonRequired] int Offset, int Size,
    [property: JsonRequired] int ArrayStride, [property: JsonRequired] int MatrixStride);

public sealed record GaResourceLayout(
    int Size, int Alignment, [property: JsonRequired] int ElementStride, IReadOnlyList<GaLayoutField> Fields);

public sealed record GaResourceBinding(
    string LogicalResourceId, [property: JsonRequired] GaBindingNamespace Namespace,
    [property: JsonRequired] int Space, [property: JsonRequired] int Index, int Count,
    [property: JsonRequired] GaResourceKind Kind, [property: JsonRequired] GaResourceAccess Access,
    GaResourceLayout? Layout);

public sealed record GaResourceUsage(string LogicalResourceId, [property: JsonRequired] bool Used);

public static class GaComputeProfiles
{
    public const string Portable32 = "Portable32";
    public const string WideFixedEmulated = "WideFixedEmulated";
    public const string NativeWide = "NativeWide";
}

public sealed record GaKernelRequirements(
    string ComputeProfile, IReadOnlyList<string> RequiredFeatures, int WorkgroupX, int WorkgroupY,
    int WorkgroupZ, [property: JsonRequired] int SharedMemoryBytes, [property: JsonRequired] long StorageBytes);

public sealed record GaArtifactManifest(
    int SchemaVersion, GaContractIdentity Contract, GaToolchain Toolchain, string EntryPoint,
    IReadOnlyList<GaResourceBinding> DeclaredBindings, IReadOnlyList<GaResourceUsage> EntryPointUsage,
    GaKernelRequirements Requirements, string ArtifactDigest, string ReflectionDigest,
    string SourceMapDigest)
{
    public const int CurrentSchemaVersion = 1;

    public byte[] GetCanonicalBytes() =>
        JsonSerializer.SerializeToUtf8Bytes(Normalize(), GaManifestJsonContext.Default.GaArtifactManifest);

    public string GetContentId() => ComputeDigest(GetCanonicalBytes());

    public static GaArtifactManifest Parse(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize(bytes, GaManifestJsonContext.Default.GaArtifactManifest)
            ?? throw Invalid("Manifest is missing.");
        return manifest.Normalize();
    }

    public static string ComputeDigest(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal GaArtifactManifest Normalize()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw Invalid("Unsupported manifest schema version.");
        if (Contract is null || Contract.NumericVersion < 1 || Contract.SubsetVersion < 1 || Contract.IrVersion < 1)
            throw Invalid("Invalid contract versions.");
        if (Contract.EventCapacity is < 0 or > GaEventPacket.MaximumCapacityPerSource ||
            Contract.EventCapacity != 0 && (Contract.SubsetVersion < 2 || Contract.IrVersion < 2))
            throw Invalid("Invalid contract event capacity.");
        if (Contract.UsesSnapshot && (Contract.SubsetVersion < 3 || Contract.IrVersion < 3))
            throw Invalid("Invalid contract version for scalar snapshot lookup.");
        ValidateText(Contract.SystemId, "system ID");
        ValidateText(Contract.SimulationContractId, "simulation contract ID");
        ValidateDigest(Contract.SourceDigest, "source");
        ValidateDigest(Contract.IrDigest, "IR");
        ValidateDigest(Contract.ComponentSchemaDigest, "component schema");
        ValidateText(EntryPoint, "entry point");
        ValidateDigest(ArtifactDigest, "artifact");
        ValidateDigest(ReflectionDigest, "reflection");
        ValidateDigest(SourceMapDigest, "source map");

        if (Toolchain is null || !Enum.IsDefined(Toolchain.Format))
            throw Invalid("Missing or unknown target format.");
        ValidateText(Toolchain.SlangVersion, "Slang version");
        ValidateText(Toolchain.DownstreamVersion, "downstream toolchain version");
        ValidateText(Toolchain.TargetProfile, "target profile");
        var toolchain = Toolchain with
        {
            Options = CopyText(Toolchain.Options, "compiler options", ordered: true),
            Defines = CopyText(Toolchain.Defines, "compiler defines", ordered: false),
            Specializations = CopyText(Toolchain.Specializations, "specializations", ordered: false)
        };

        if (Requirements is null || Requirements.WorkgroupX < 1 || Requirements.WorkgroupY < 1 ||
            Requirements.WorkgroupZ < 1 || Requirements.SharedMemoryBytes < 0 || Requirements.StorageBytes < 0)
            throw Invalid("Invalid compute requirements.");
        if (Requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or
            GaComputeProfiles.WideFixedEmulated or GaComputeProfiles.NativeWide))
            throw Invalid("Unknown compute profile.");
        var requirements = Requirements with
        {
            RequiredFeatures = CopyText(Requirements.RequiredFeatures, "device features", ordered: false)
        };

        var bindings = CopyOf(DeclaredBindings, "declared bindings").Select(NormalizeBinding)
            .OrderBy(binding => binding.LogicalResourceId, StringComparer.Ordinal).ToArray();
        var declaredIds = new HashSet<string>(StringComparer.Ordinal);
        var occupied = new Dictionary<(GaBindingNamespace, int), List<(int First, long End)>>();
        foreach (var binding in bindings)
        {
            if (!declaredIds.Add(binding.LogicalResourceId))
                throw Invalid("Duplicate logical resource ID.");
            var key = (binding.Namespace, binding.Space);
            if (!occupied.TryGetValue(key, out var slots))
                occupied.Add(key, slots = new List<(int, long)>());
            var end = (long)binding.Index + PhysicalWidth(binding);
            if (end > (long)int.MaxValue + 1 || slots.Any(slot => binding.Index < slot.End && slot.First < end))
                throw Invalid("Overlapping or out-of-range physical resource bindings.");
            slots.Add((binding.Index, end));
        }

        var usage = CopyOf(EntryPointUsage, "entry-point usage")
            .OrderBy(item => item.LogicalResourceId, StringComparer.Ordinal).ToArray();
        foreach (var item in usage)
        {
            if (!declaredIds.Remove(item.LogicalResourceId))
                throw Invalid("Duplicate or undeclared entry-point resource usage.");
        }
        if (declaredIds.Count != 0)
            throw Invalid("Entry-point usage must be explicit for every declared resource.");

        return this with
        {
            Toolchain = toolchain,
            DeclaredBindings = Array.AsReadOnly(bindings),
            EntryPointUsage = Array.AsReadOnly(usage),
            Requirements = requirements
        };
    }

    private GaResourceBinding NormalizeBinding(GaResourceBinding binding)
    {
        ValidateText(binding.LogicalResourceId, "logical resource ID");
        if (!Enum.IsDefined(binding.Namespace) || !Enum.IsDefined(binding.Kind) || !Enum.IsDefined(binding.Access) ||
            binding.Space < 0 || binding.Index < 0 || binding.Count < 1)
            throw Invalid("Invalid resource binding.");
        var validNamespace = Toolchain.Format switch
        {
            GaArtifactFormat.SpirV => binding.Namespace is GaBindingNamespace.VulkanDescriptor or
                GaBindingNamespace.VulkanPushConstants,
            GaArtifactFormat.Dxil or GaArtifactFormat.Dxbc => binding.Namespace is GaBindingNamespace.D3DConstantBuffer
                or GaBindingNamespace.D3DResource or GaBindingNamespace.D3DUnorderedAccess or GaBindingNamespace.D3DSampler,
            GaArtifactFormat.MetalLibrary => binding.Namespace is GaBindingNamespace.MetalBuffer
                or GaBindingNamespace.MetalTexture or GaBindingNamespace.MetalSampler,
            GaArtifactFormat.Wgsl => binding.Namespace == GaBindingNamespace.WebGpuBinding,
            _ => false
        };
        if (!validNamespace)
            throw Invalid("Resource binding namespace does not match the target format.");
        if ((binding.Namespace == GaBindingNamespace.VulkanPushConstants) !=
            (Toolchain.Format == GaArtifactFormat.SpirV && binding.Kind == GaResourceKind.PushConstants) ||
            (binding.Namespace == GaBindingNamespace.VulkanPushConstants && (binding.Space != 0 || binding.Count != 1)))
            throw Invalid("Invalid Vulkan push-constant binding.");
        if (binding.Kind is GaResourceKind.UniformBuffer or GaResourceKind.PushConstants or GaResourceKind.Sampler &&
            binding.Access != GaResourceAccess.Read)
            throw Invalid("Resource kind requires read-only access.");
        if (binding.Kind is GaResourceKind.Sampler or GaResourceKind.Texture)
        {
            if (binding.Layout is not null)
                throw Invalid("Texture and sampler bindings cannot carry buffer layout.");
            return binding;
        }

        var layout = binding.Layout;
        if (layout is null || layout.Size < 1 || layout.Alignment < 1 ||
            (layout.Alignment & (layout.Alignment - 1)) != 0 || layout.ElementStride < 0 ||
            (layout.ElementStride != 0 &&
                (layout.ElementStride < layout.Size || layout.ElementStride % layout.Alignment != 0)))
            throw Invalid("Invalid buffer layout.");
        var fields = CopyOf(layout.Fields, "layout fields")
            .OrderBy(field => field.Path, StringComparer.Ordinal).ToArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            ValidateText(field.Path, "field path");
            if (!paths.Add(field.Path) || field.Offset < 0 || field.Size < 1 ||
                (long)field.Offset + field.Size > layout.Size || field.ArrayStride < 0 ||
                field.MatrixStride < 0 || field.ArrayStride > field.Size || field.MatrixStride > field.Size)
                throw Invalid("Invalid or duplicate buffer field.");
        }
        return binding with { Layout = layout with { Fields = Array.AsReadOnly(fields) } };
    }

    private static int PhysicalWidth(GaResourceBinding binding) =>
        binding.Namespace is GaBindingNamespace.VulkanDescriptor or GaBindingNamespace.WebGpuBinding ? 1 : binding.Count;

    private static IReadOnlyList<string> CopyText(IReadOnlyList<string> values, string role, bool ordered)
    {
        var copy = CopyOf(values, role);
        foreach (var value in copy)
            ValidateText(value, role);
        if (!ordered)
        {
            Array.Sort(copy, StringComparer.Ordinal);
            for (var i = 1; i < copy.Length; i++)
                if (copy[i] == copy[i - 1])
                    throw Invalid($"Duplicate {role}.");
        }
        return Array.AsReadOnly(copy);
    }

    private static T[] CopyOf<T>(IReadOnlyList<T> values, string role) where T : class
    {
        var copy = values?.ToArray();
        if (copy is null || copy.Any(item => item is null))
            throw Invalid($"Missing {role}.");
        return copy;
    }

    internal static void ValidateText(string value, string role)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw Invalid($"Missing or invalid {role}.");
    }

    internal static void ValidateDigest(string value, string role)
    {
        if (value is null || value.Length != 64 || value.Any(ch => ch is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Invalid($"Invalid SHA-256 digest for {role}.");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GaArtifactManifest))]
[JsonSerializable(typeof(GaSourceSpan[]))]
internal sealed partial class GaManifestJsonContext : JsonSerializerContext;
