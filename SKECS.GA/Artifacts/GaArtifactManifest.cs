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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotSchema { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TickResourceType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TickResourceWords { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BufferSchema { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BufferRowWords { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesCommands { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ComponentRemovalCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ComponentAdditionCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AdditionPayloadWords { get; init; }
    /// <summary>The number of typed output bindings, including reduction leaves.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EventTypeCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EventPayloadWords { get; init; }
    /// <summary>The number of typed output bindings that feed runner reductions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReductionTypeCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesDomainOrder { get; init; }
}

public enum GaArtifactFormat { SpirV, Dxil, Dxbc, MetalLibrary, Wgsl, Glsl, UnityCompute }

public sealed record GaToolchain(
    string SlangVersion, string DownstreamVersion, [property: JsonRequired] GaArtifactFormat Format, string TargetProfile,
    IReadOnlyList<string> Options, IReadOnlyList<string> Defines, IReadOnlyList<string> Specializations);

public enum GaResourceKind { StorageBuffer, UniformBuffer, PushConstants, Texture, Sampler }
public enum GaResourceAccess { Read, Write, ReadWrite }
public enum GaBindingNamespace
{
    VulkanDescriptor, VulkanPushConstants, D3DConstantBuffer, D3DResource, D3DUnorderedAccess,
    D3DSampler, MetalBuffer, MetalTexture, MetalSampler, WebGpuBinding, OpenGlShaderStorage, UnityComputeProperty
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

    public static string ComputeDigest(ReadOnlySpan<byte> bytes)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
#else
        using var sha256 = SHA256.Create();
        Span<byte> digest = stackalloc byte[32];
        if (!sha256.TryComputeHash(bytes, digest, out _))
            throw new CryptographicException("The SHA-256 digest could not be computed.");

        const string hex = "0123456789abcdef";
        Span<char> chars = stackalloc char[64];
        for (var i = 0; i < digest.Length; i++)
        {
            chars[2 * i] = hex[digest[i] >> 4];
            chars[2 * i + 1] = hex[digest[i] & 15];
        }
        return new string(chars);
#endif
    }

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
        if (Contract.SnapshotSchema is { } snapshotSchema &&
            (!Contract.UsesSnapshot || Contract.SubsetVersion < 9 || Contract.IrVersion < 9 ||
                !GaSnapshotLayout.IsValidSchema(snapshotSchema)))
            throw Invalid("Invalid contract schema for typed snapshot lookup.");
        if (Contract.TickResourceWords < 0 || Contract.TickResourceWords != 0 && Contract.TickResourceType is null)
            throw Invalid("Invalid contract word count for the tick resource.");
        if (Contract.TickResourceType is { } tickResourceType)
        {
            if (Contract.SubsetVersion < 12 || Contract.IrVersion < 12)
                throw Invalid("Invalid contract version for the tick resource.");
            ValidateText(tickResourceType, "tick resource type");
        }
        if (Contract.BufferRowWords < 0 || Contract.BufferRowWords != 0 && Contract.BufferSchema is null)
            throw Invalid("Invalid contract row width for the read buffer.");
        if (Contract.BufferSchema is { } bufferSchema)
        {
            if (Contract.SubsetVersion < 13 || Contract.IrVersion < 13)
                throw Invalid("Invalid contract version for the read buffer.");
            ValidateDigest(bufferSchema, "read buffer schema");
        }
        if (Contract.UsesCommands && (Contract.SubsetVersion < 4 || Contract.IrVersion < 4))
            throw Invalid("Invalid contract version for structural commands.");
        if (Contract.ComponentRemovalCount is < 0 or > GaOutputRecord.MaximumComponentRemovals ||
            Contract.ComponentRemovalCount != 0 && (!Contract.UsesCommands || Contract.SubsetVersion < 6 || Contract.IrVersion < 6))
            throw Invalid("Invalid contract component removal table.");
        if (Contract.ComponentAdditionCount is < 0 or > GaOutputRecord.MaximumComponentCommands ||
            Contract.AdditionPayloadWords is < 0 or > GaOutputRecord.MaximumAdditionPayloadWords ||
            Contract.ComponentAdditionCount == 0 && Contract.AdditionPayloadWords != 0 ||
            Contract.ComponentAdditionCount != 0 && (!Contract.UsesCommands || Contract.SubsetVersion < 7 || Contract.IrVersion < 7))
            throw Invalid("Invalid contract component addition table.");
        if (Contract.EventTypeCount is < 0 or > GaOutputRecord.MaximumEventTypes ||
            Contract.EventPayloadWords is < 0 or > GaOutputRecord.MaximumEventPayloadWords ||
            Contract.EventTypeCount == 0 && Contract.EventPayloadWords != 0 ||
            Contract.EventTypeCount != 0 && (Contract.SubsetVersion < 14 || Contract.IrVersion < 14))
            throw Invalid("Invalid contract typed event table.");
        if (Contract.ReductionTypeCount < 0 || Contract.ReductionTypeCount > Contract.EventTypeCount ||
            Contract.ReductionTypeCount != 0 && (Contract.SubsetVersion < 15 || Contract.IrVersion < 15))
            throw Invalid("Invalid contract reduction bindings.");
        if (Contract.UsesDomainOrder && (Contract.SubsetVersion < 8 || Contract.IrVersion < 8))
            throw Invalid("Invalid contract version for domain-ordered outputs.");
        ValidateText(Contract.SystemId, "system ID");
        ValidateText(Contract.SimulationContractId, "simulation contract ID");
        ValidateDigest(Contract.SourceDigest, "source");
        ValidateDigest(Contract.IrDigest, "IR");
        ValidateDigest(Contract.ComponentSchemaDigest, "component schema");
        ValidateText(EntryPoint, "entry point");
        ValidateDigest(ArtifactDigest, "artifact");
        ValidateDigest(ReflectionDigest, "reflection");
        ValidateDigest(SourceMapDigest, "source map");

        if (Toolchain is null || !GaEnum.IsDefined(Toolchain.Format))
            throw Invalid("Missing or unknown target format.");
        ValidateText(Toolchain.SlangVersion, "Slang version");
        ValidateText(Toolchain.DownstreamVersion, "downstream toolchain version");
        ValidateText(Toolchain.TargetProfile, "target profile");
        if (Toolchain.Format == GaArtifactFormat.UnityCompute &&
            (Toolchain.TargetProfile != GaUnityComputeAbi.TargetProfile || EntryPoint != GaUnityComputeAbi.MainKernel))
            throw Invalid("Invalid Unity compute target profile or entry point.");
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
        if (!GaEnum.IsDefined(binding.Namespace) || !GaEnum.IsDefined(binding.Kind) || !GaEnum.IsDefined(binding.Access) ||
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
            GaArtifactFormat.Glsl => binding.Namespace == GaBindingNamespace.OpenGlShaderStorage &&
                binding.Space == 0 && binding.Kind == GaResourceKind.StorageBuffer,
            GaArtifactFormat.UnityCompute => binding.Namespace == GaBindingNamespace.UnityComputeProperty &&
                binding.Space == 0 && binding.Count == 1 &&
                binding.Kind is GaResourceKind.StorageBuffer or GaResourceKind.UniformBuffer,
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
