using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SKECS.GA.Backend.D3D12;

public sealed class GaD3D12Backend : IDisposable
{
    private const uint MinimumShaderModel = 0x60;
    private readonly object _gate = new();
    private readonly GaD3D12Native _api;
    private readonly GaD3D12Native.Lease _context;
    private readonly HashSet<uint> _probed = new();
    private bool _disposed;

    private GaD3D12Backend(GaD3D12Native api, GaD3D12Native.Lease context)
    {
        _api = api;
        _context = context;
        var devices = new IGaDevice[api.GetDeviceCount(context)];
        for (uint i = 0; i < devices.Length; i++)
            devices[i] = new Device(this, i, api.GetDevice(context, i));
        Devices = Array.AsReadOnly(devices);
    }

    public IReadOnlyList<IGaDevice> Devices { get; }

    public static GaD3D12Backend Open(string nativeLibraryPath)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The GA D3D12 adapter requires a Windows x64 process.");

        var api = new GaD3D12Native(nativeLibraryPath);
        GaD3D12Native.Lease? context = null;
        try
        {
            context = api.OpenContext();
            return new GaD3D12Backend(api, context);
        }
        catch
        {
            context?.Dispose();
            api.Dispose();
            throw;
        }
    }

    public static bool TryOpen(
        string nativeLibraryPath, [NotNullWhen(true)] out GaD3D12Backend? backend, out string? error)
    {
        try
        {
            backend = Open(nativeLibraryPath);
            error = null;
            return true;
        }
        catch (Exception failure) when (failure is GaD3D12Exception or DllNotFoundException or BadImageFormatException
            or EntryPointNotFoundException or PlatformNotSupportedException)
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

    private GaPreparation Prepare(uint index, GaD3D12Native.DeviceProperties device, GaArtifactBundle artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var manifest = artifact.Manifest;
            var requirements = manifest.Requirements;
            if (manifest.Toolchain.Format != GaArtifactFormat.Dxil || manifest.Toolchain.TargetProfile != "sm_6_0" ||
                manifest.EntryPoint != "main" || artifact.Artifact.Length % sizeof(uint) != 0 ||
                requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated) ||
                requirements.WorkgroupY != 1 || requirements.WorkgroupZ != 1)
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The D3D12 adapter requires the main entry of a one-dimensional DXIL Shader Model 6.0 GA kernel.");

            if (device.ShaderModel < MinimumShaderModel || requirements.RequiredFeatures.Count != 0)
                return new GaPreparation(null, GaStartupReason.MissingCapability,
                    "The D3D12 adapter supports Shader Model 6.0 GA kernels with no extra device features.");
            if (requirements.WorkgroupX > device.MaxWorkgroupX ||
                requirements.WorkgroupX > device.MaxWorkgroupInvocations ||
                requirements.SharedMemoryBytes != 0 ||
                requirements.StorageBytes > (long)device.MaxStorageBufferRange * 3 ||
                device.MaxGroupsX == 0 || device.MaxStorageBufferRange < sizeof(uint))
                return new GaPreparation(null, GaStartupReason.ResourceBudgetExceeded,
                    "The D3D12 kernel exceeds the adapter's supported workgroup or storage limits.");

            Span<GaD3D12Native.Location> locations = stackalloc GaD3D12Native.Location[3];
            if (!TryMapBindings(manifest, locations))
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest,
                    "The D3D12 kernel needs one uint32 SRV input and two uint32 UAV buffers for output and faults.");

            IReadOnlyList<GaSourceSpan> sourceMap;
            try
            {
                sourceMap = artifact.DecodeSourceMap();
            }
            catch (Exception failure) when (failure is InvalidDataException or JsonException)
            {
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest, failure.Message);
            }

            uint workgroup = (uint)requirements.WorkgroupX;
            GaD3D12Native.Lease handle;
            try
            {
                handle = _api.CreateKernel(_context, index, artifact.Artifact, workgroup, locations);
            }
            catch (GaD3D12Exception failure)
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
                    new GaD3D12Kernel(_api, handle, workgroup, device.MaxGroupsX, device.MaxStorageBufferRange,
                        sourceMap, manifest.Contract.TickResourceWords,
                        manifest.Contract.BufferSchema is null ? null : manifest.Contract.BufferRowWords),
                    GaStartupReason.None);
            }
            catch (GaD3D12Exception failure)
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

    private static bool TryMapBindings(GaArtifactManifest manifest, Span<GaD3D12Native.Location> locations)
    {
        int used = 0;
        foreach (var usage in manifest.EntryPointUsage)
        {
            if (!usage.Used)
                continue;
            int slot = usage.LogicalResourceId switch
            {
                "gaInput" => 0,
                "gaOutput" => 1,
                "gaFaults" => 2,
                _ => -1
            };
            if (slot < 0)
                return false;
            var binding = manifest.DeclaredBindings.First(binding => binding.LogicalResourceId == usage.LogicalResourceId);
            var bindingNamespace = slot == 0 ? GaBindingNamespace.D3DResource : GaBindingNamespace.D3DUnorderedAccess;
            var access = slot == 0 ? GaResourceAccess.Read : GaResourceAccess.ReadWrite;
            if (binding.Namespace != bindingNamespace || binding.Access != access ||
                binding.Kind != GaResourceKind.StorageBuffer || binding.Count != 1 ||
                binding.Layout is not { Size: sizeof(uint), Alignment: sizeof(uint), ElementStride: sizeof(uint), Fields.Count: 0 })
                return false;
            locations[slot] = new GaD3D12Native.Location((uint)binding.Space, (uint)binding.Index);
            used++;
        }
        return used == locations.Length;
    }

    private sealed class Device : IGaDevice
    {
        private readonly GaD3D12Backend _backend;
        private readonly uint _index;
        private readonly GaD3D12Native.DeviceProperties _properties;

        internal Device(GaD3D12Backend backend, uint index, GaD3D12Native.DeviceProperties properties)
        {
            _backend = backend;
            _index = index;
            _properties = properties;
            string driver = properties.DriverVersionLow == 0 && properties.DriverVersionHigh == 0
                ? "Unavailable"
                : $"{properties.DriverVersionHigh >> 16}.{properties.DriverVersionHigh & 0xffff}." +
                  $"{properties.DriverVersionLow >> 16}.{properties.DriverVersionLow & 0xffff}";
            Info = new GaDeviceInfo("SKECS.GA D3D12", "D3D12",
                $"SM {properties.ShaderModel >> 4}.{properties.ShaderModel & 0xf}",
                $"{properties.GetName()} ({properties.VendorId:x4}:{properties.DeviceId:x4}, {properties.GetLuid()})", driver);
            var portable = new GaDeviceProfile(GaComputeProfiles.Portable32, GaQualification.Experimental, [],
                ToInt(properties.MaxWorkgroupX), 1, 1, ToInt(properties.MaxWorkgroupInvocations), 0,
                (long)properties.MaxStorageBufferRange * 3);
            Profiles = properties.ShaderModel >= MinimumShaderModel
                ? Array.AsReadOnly(new[] { portable, portable with { Profile = GaComputeProfiles.WideFixedEmulated } })
                : [];
        }

        public GaDeviceInfo Info { get; }
        public GaArtifactFormat Format => GaArtifactFormat.Dxil;
        public IReadOnlyList<GaDeviceProfile> Profiles { get; }
        public GaPreparation Prepare(GaArtifactBundle artifact) => _backend.Prepare(_index, _properties, artifact);

        private static int ToInt(uint value) => (int)Math.Min(value, (uint)int.MaxValue);
    }
}
