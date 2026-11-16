using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SKECS.GA.Backend.Vulkan;

public sealed class GaVulkanBackend : IDisposable
{
    private readonly object _gate = new();
    private readonly GaVulkanNative _api;
    private readonly GaVulkanNative.Lease _context;
    private readonly HashSet<uint> _probed = new();
    private bool _disposed;

    private GaVulkanBackend(GaVulkanNative api, GaVulkanNative.Lease context)
    {
        _api = api;
        _context = context;
        var devices = new IGaDevice[_api.GetDeviceCount(_context)];
        for (uint i = 0; i < devices.Length; i++)
            devices[i] = new Device(this, i, _api.GetDevice(_context, i));
        Devices = Array.AsReadOnly(devices);
    }

    public IReadOnlyList<IGaDevice> Devices { get; }

    public static GaVulkanBackend Open(string nativeLibraryPath)
    {
        var api = new GaVulkanNative(nativeLibraryPath);
        GaVulkanNative.Lease? context = null;
        try
        {
            context = api.OpenContext();
            return new GaVulkanBackend(api, context);
        }
        catch
        {
            context?.Dispose();
            api.Dispose();
            throw;
        }
    }

    public static bool TryOpen(
        string nativeLibraryPath, [NotNullWhen(true)] out GaVulkanBackend? backend, out string? error)
    {
        try
        {
            backend = Open(nativeLibraryPath);
            error = null;
            return true;
        }
        catch (Exception failure) when (failure is GaVulkanException or DllNotFoundException
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

    private GaPreparation Prepare(uint index, GaVulkanNative.DeviceProperties device, GaArtifactBundle artifact)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var manifest = artifact.Manifest;
            if (manifest.Toolchain.Format != GaArtifactFormat.SpirV ||
                manifest.Toolchain.TargetProfile != "spirv_1_3" ||
                manifest.Requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated) ||
                manifest.Requirements.WorkgroupY != 1 || manifest.Requirements.WorkgroupZ != 1)
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The Vulkan adapter requires SPIR-V 1.3 and a one-dimensional GA kernel using 32-bit device operations.");

            if (device.MaxPerStageStorageBuffers < 3 || device.MaxDescriptorSetStorageBuffers < 3)
                return new GaPreparation(null, GaStartupReason.ResourceBudgetExceeded,
                    "The Vulkan device needs three compute storage buffer descriptors.");
            var locations = new GaVulkanNative.Location[3];
            if (!TryMapBindings(manifest, device.MaxBoundDescriptorSets, locations))
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The Vulkan kernel needs three used and mapped scalar storage buffers.");
            IReadOnlyList<GaSourceSpan> sourceMap;
            try
            {
                sourceMap = artifact.DecodeSourceMap();
            }
            catch (Exception failure) when (failure is InvalidDataException or JsonException)
            {
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest, failure.Message);
            }

            var workgroup = (uint)manifest.Requirements.WorkgroupX;
            GaVulkanNative.Lease handle;
            try
            {
                handle = _api.CreateKernel(_context, index, artifact.Artifact, manifest.EntryPoint, workgroup, locations);
            }
            catch (GaVulkanException failure)
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
                    new GaVulkanKernel(_api, handle, workgroup, device.MaxGroupsX, device.MaxStorageBufferRange,
                        sourceMap, manifest.Contract.TickResourceWords,
                        manifest.Contract.BufferSchema is null ? null : manifest.Contract.BufferRowWords),
                    GaStartupReason.None);
            }
            catch (GaVulkanException failure)
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

    private static bool TryMapBindings(
        GaArtifactManifest manifest, uint maxBoundDescriptorSets, Span<GaVulkanNative.Location> locations)
    {
        int used = 0;
        foreach (var usage in manifest.EntryPointUsage)
        {
            if (!usage.Used)
                continue;
            var slot = usage.LogicalResourceId switch
            {
                "gaInput" => 0,
                "gaOutput" => 1,
                "gaFaults" => 2,
                _ => -1
            };
            if (slot < 0)
                return false;
            var binding = manifest.DeclaredBindings.First(binding => binding.LogicalResourceId == usage.LogicalResourceId);
            var accessMatches = slot == 0
                ? binding.Access == GaResourceAccess.Read
                : binding.Access is GaResourceAccess.Write or GaResourceAccess.ReadWrite;
            if (binding.Namespace != GaBindingNamespace.VulkanDescriptor || binding.Kind != GaResourceKind.StorageBuffer ||
                binding.Count != 1 || binding.Space >= maxBoundDescriptorSets || !accessMatches ||
                binding.Layout is not { Size: sizeof(uint), Alignment: sizeof(uint), ElementStride: sizeof(uint) })
                return false;
            locations[slot] = new GaVulkanNative.Location((uint)binding.Space, (uint)binding.Index);
            used++;
        }
        return used == locations.Length;
    }

    private sealed class Device : IGaDevice
    {
        private readonly GaVulkanBackend _backend;
        private readonly uint _index;
        private readonly GaVulkanNative.DeviceProperties _properties;

        public Device(GaVulkanBackend backend, uint index, GaVulkanNative.DeviceProperties properties)
        {
            _backend = backend;
            _index = index;
            _properties = properties;
            var major = (properties.ApiVersion >> 22) & 0x7f;
            var minor = (properties.ApiVersion >> 12) & 0x3ff;
            var patch = properties.ApiVersion & 0xfff;
            Info = new GaDeviceInfo("SKECS.GA Vulkan", "Vulkan", $"{major}.{minor}.{patch}",
                $"{properties.GetName()} ({properties.VendorId:x4}:{properties.DeviceId:x4}, {properties.GetUuid()})",
                $"0x{properties.DriverVersion:x8}");
            var portable = new GaDeviceProfile(GaComputeProfiles.Portable32, GaQualification.Experimental, [],
                ToInt(properties.MaxWorkgroupX), 1, 1, ToInt(properties.MaxWorkgroupInvocations), 0,
                (long)properties.MaxStorageBufferRange * 3);
            Profiles = major > 1 || major == 1 && minor >= 1
                ? [portable, portable with { Profile = GaComputeProfiles.WideFixedEmulated }]
                : [];
        }

        public GaDeviceInfo Info { get; }
        public GaArtifactFormat Format => GaArtifactFormat.SpirV;
        public IReadOnlyList<GaDeviceProfile> Profiles { get; }
        public GaPreparation Prepare(GaArtifactBundle artifact) => _backend.Prepare(_index, _properties, artifact);

        private static int ToInt(uint value) => (int)Math.Min(value, (uint)int.MaxValue);
    }
}
