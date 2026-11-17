using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SKECS.GA.Backend.Metal;

public sealed class GaMetalBackend : IDisposable
{
    private const uint MaxGroupsX = 65_535;
    private readonly object _gate = new();
    private readonly GaMetalNative _api;
    private readonly GaMetalNative.Lease _context;
    private readonly HashSet<uint> _probed = [];
    private bool _disposed;

    private GaMetalBackend(GaMetalNative api, GaMetalNative.Lease context)
    {
        _api = api;
        _context = context;
        var devices = new IGaDevice[_api.GetDeviceCount(_context)];
        for (uint i = 0; i < devices.Length; i++)
            devices[i] = new Device(this, i, _api.GetDevice(_context, i));
        Devices = Array.AsReadOnly(devices);
    }

    public IReadOnlyList<IGaDevice> Devices { get; }

    public static GaMetalBackend Open(string nativeLibraryPath)
    {
        var api = new GaMetalNative(nativeLibraryPath);
        GaMetalNative.Lease? context = null;
        try
        {
            context = api.OpenContext();
            return new GaMetalBackend(api, context);
        }
        catch
        {
            context?.Dispose();
            api.Dispose();
            throw;
        }
    }

    public static bool TryOpen(
        string nativeLibraryPath, [NotNullWhen(true)] out GaMetalBackend? backend, out string? error)
    {
        try
        {
            backend = Open(nativeLibraryPath);
            error = null;
            return true;
        }
        catch (Exception failure) when (failure is GaMetalException or DllNotFoundException
            or BadImageFormatException or EntryPointNotFoundException)
        {
            backend = null;
            error = failure.Message;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _context.Dispose();
            _api.Dispose();
        }
    }

    private GaPreparation Prepare(uint index, GaMetalNative.DeviceProperties device, GaArtifactBundle artifact)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var manifest = artifact.Manifest;
            if (manifest.Toolchain.Format != GaArtifactFormat.MetalLibrary ||
                manifest.Toolchain.TargetProfile != TargetProfile(device.Platform) ||
                manifest.Requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated) ||
                manifest.Requirements.WorkgroupY != 1 || manifest.Requirements.WorkgroupZ != 1)
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The Metal adapter requires a platform-matched Metal 2.3 library with a one-dimensional portable GA kernel.");

            Span<uint> locations = stackalloc uint[4];
            if (!TryMapBindings(manifest, locations))
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The Metal kernel requires three used scalar storage buffers and supports one input byte-length uniform.");

            IReadOnlyList<GaSourceSpan> sourceMap;
            try
            {
                if (!MatchesReflection(artifact, device.Platform, locations))
                    return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                        "Metal reflection disagrees with the platform, workgroup or physical bindings in the manifest.");
                sourceMap = artifact.DecodeSourceMap();
            }
            catch (Exception failure) when (failure is InvalidDataException or JsonException)
            {
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest, failure.Message);
            }

            uint workgroup = (uint)manifest.Requirements.WorkgroupX;
            GaMetalNative.Lease handle;
            try { handle = _api.CreateKernel(_context, index, artifact.Artifact, manifest.EntryPoint, workgroup, locations); }
            catch (GaMetalException failure)
            {
                return new GaPreparation(null, GaStartupReason.PipelineCreationFailed, failure.Message);
            }

            try
            {
                if (!_probed.Contains(index))
                {
                    _api.Probe(_context, index);
                    _probed.Add(index);
                }
                return new GaPreparation(
                    new GaMetalKernel(_api, handle, workgroup, MaxGroupsX, device.MaxStorageBufferRange,
                        sourceMap, manifest.Contract.TickResourceWords,
                        manifest.Contract.BufferSchema is null ? null : manifest.Contract.BufferRowWords),
                    GaStartupReason.None);
            }
            catch (GaMetalException failure)
            {
                handle.Dispose();
                return new GaPreparation(null, GaStartupReason.ProbeFailed, failure.Message);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    private static bool TryMapBindings(GaArtifactManifest manifest, Span<uint> locations)
    {
        locations.Fill(uint.MaxValue);
        foreach (var usage in manifest.EntryPointUsage)
        {
            if (!usage.Used)
                continue;
            int slot = usage.LogicalResourceId switch
            {
                "gaInput" => 0,
                "gaOutput" => 1,
                "gaFaults" => 2,
                "gaInputLength" => 3,
                _ => -1
            };
            if (slot < 0)
                return false;

            var binding = manifest.DeclaredBindings.First(binding => binding.LogicalResourceId == usage.LogicalResourceId);
            bool accessMatches = slot is 0 or 3 ? binding.Access == GaResourceAccess.Read
                : binding.Access is GaResourceAccess.Write or GaResourceAccess.ReadWrite;
            var kind = slot == 3 ? GaResourceKind.UniformBuffer : GaResourceKind.StorageBuffer;
            if (binding.Namespace != GaBindingNamespace.MetalBuffer || binding.Kind != kind ||
                binding.Space != 0 || binding.Index >= 31 || binding.Count != 1 || !accessMatches ||
                binding.Layout is not { Size: sizeof(uint), Alignment: sizeof(uint), Fields.Count: 0 } layout ||
                (slot == 3 ? layout.ElementStride is not (0 or sizeof(uint)) : layout.ElementStride != sizeof(uint)))
                return false;
            locations[slot] = (uint)binding.Index;
        }
        return locations[..3].IndexOf(uint.MaxValue) < 0;
    }

    private static bool MatchesReflection(GaArtifactBundle artifact, uint platform, ReadOnlySpan<uint> locations)
    {
        // The pipeline validates active arguments, while the SPIR-V reflection records the fixed workgroup.
        using var reflection = JsonDocument.Parse(artifact.Reflection.ToArray());
        var root = reflection.RootElement;
        var manifest = artifact.Manifest;
        if (root.ValueKind != JsonValueKind.Object ||
            !MatchesString(root, "Platform", platform == 1 ? "macos" : "ios") ||
            !MatchesString(root, "MetalLanguageVersion", "2.3") ||
            !MatchesString(root, "EntryPoint", manifest.EntryPoint) ||
            !root.TryGetProperty("WorkgroupSize", out var size) || size.ValueKind != JsonValueKind.Array ||
            size.GetArrayLength() != 3 || !MatchesNumber(size[0], (uint)manifest.Requirements.WorkgroupX) ||
            !MatchesNumber(size[1], 1) || !MatchesNumber(size[2], 1) ||
            !root.TryGetProperty("StorageSlots", out var buffers) || buffers.ValueKind != JsonValueKind.Object ||
            buffers.EnumerateObject().Count() != 3 ||
            !MatchesSlot(buffers, "gaInput", locations[0]) ||
            !MatchesSlot(buffers, "gaOutput", locations[1]) ||
            !MatchesSlot(buffers, "gaFaults", locations[2]) ||
            !root.TryGetProperty("InputLengthInBytesSlot", out var lengthSlot))
            return false;

        return locations[3] == uint.MaxValue ? lengthSlot.ValueKind == JsonValueKind.Null :
            MatchesNumber(lengthSlot, locations[3]);

        static bool MatchesString(JsonElement value, string property, string expected) =>
            value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && item.ValueEquals(expected);
        static bool MatchesSlot(JsonElement value, string property, uint expected) =>
            value.TryGetProperty(property, out var item) && MatchesNumber(item, expected);
        static bool MatchesNumber(JsonElement value, uint expected) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint number) && number == expected;
    }

    private static string TargetProfile(uint platform) => platform switch
    {
        1 => "metal_2_3-macos",
        2 => "metal_2_3-ios",
        _ => throw new GaMetalException($"Unsupported Metal host platform {platform}.")
    };

    private sealed class Device : IGaDevice
    {
        private readonly GaMetalBackend _backend;
        private readonly uint _index;
        private readonly GaMetalNative.DeviceProperties _properties;

        public Device(GaMetalBackend backend, uint index, GaMetalNative.DeviceProperties properties)
        {
            _backend = backend;
            _index = index;
            _properties = properties;
            Info = new GaDeviceInfo("SKECS.GA Metal", "Metal", TargetProfile(properties.Platform),
                $"{properties.GetName()} (registry:{properties.RegistryId:x16})", $"OS {Environment.OSVersion.Version}");
            var portable = new GaDeviceProfile(GaComputeProfiles.Portable32, GaQualification.Experimental, [],
                (int)Math.Min(properties.MaxWorkgroupX, int.MaxValue), 1, 1,
                (int)Math.Min(properties.MaxWorkgroupInvocations, int.MaxValue), 0,
                (long)properties.MaxStorageBufferRange * 3);
            Profiles = [portable, portable with { Profile = GaComputeProfiles.WideFixedEmulated }];
        }

        public GaDeviceInfo Info { get; }
        public GaArtifactFormat Format => GaArtifactFormat.MetalLibrary;
        public IReadOnlyList<GaDeviceProfile> Profiles { get; }
        public GaPreparation Prepare(GaArtifactBundle artifact) => _backend.Prepare(_index, _properties, artifact);
    }
}
