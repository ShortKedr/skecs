using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SKECS.GA.Backend.OpenGl;

public sealed class GaOpenGlBackend : IDisposable
{
    private const string PinnedToolchain = "SPIRV-Tools v2025.1 v2025.1.rc1-0-gf289d047 " +
        "sha256:e8ced090e643ab277296039e61c19de39c23535bdebd31109e8e7db23a86c303, " +
        "SPIRV-Cross vulkan-sdk-1.4.304.0-40-g2c32b6bf " +
        "sha256:cc19353f15b3db467d58d5e0a97add7a6797fbc52313c57d38b0f77bfa2f90a1";

    private readonly object _gate = new();
    private readonly GaOpenGlNative _api;
    private readonly GaOpenGlNative.Lease _context;
    private readonly HashSet<uint> _probed = new();
    private bool _disposed;

    private GaOpenGlBackend(GaOpenGlNative api, GaOpenGlNative.Lease context)
    {
        _api = api;
        _context = context;
        var devices = new IGaDevice[api.GetDeviceCount(context)];
        for (uint i = 0; i < devices.Length; i++)
            devices[i] = new Device(this, i, api.GetDevice(context, i));
        Devices = Array.AsReadOnly(devices);
    }

    public IReadOnlyList<IGaDevice> Devices { get; }

    public static GaOpenGlBackend Open(string nativeLibraryPath)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The GA WGL adapter requires a Windows x64 process.");

        var api = new GaOpenGlNative(nativeLibraryPath);
        GaOpenGlNative.Lease? context = null;
        try
        {
            context = api.OpenContext();
            return new GaOpenGlBackend(api, context);
        }
        catch
        {
            context?.Dispose();
            api.Dispose();
            throw;
        }
    }

    public static bool TryOpen(string nativeLibraryPath, [NotNullWhen(true)] out GaOpenGlBackend? backend, out string? error)
    {
        try
        {
            backend = Open(nativeLibraryPath);
            error = null;
            return true;
        }
        catch (Exception failure) when (failure is GaOpenGlException or DllNotFoundException or BadImageFormatException
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

    private GaPreparation Prepare(uint index, GaOpenGlNative.DeviceProperties device, GaArtifactBundle artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var manifest = artifact.Manifest;
            var requirements = manifest.Requirements;
            if (manifest.Toolchain.Format != GaArtifactFormat.Glsl || manifest.Toolchain.TargetProfile != "glsl_430" ||
                manifest.EntryPoint != "main" || requirements.WorkgroupY != 1 || requirements.WorkgroupZ != 1 ||
                requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated))
                return new(null, GaStartupReason.IncompatibleManifest,
                    "OpenGL requires the main entry of a one-dimensional GLSL 430 GA kernel.");

            if (manifest.Toolchain is not
                {
                    SlangVersion: "2025.6.1",
                    DownstreamVersion: PinnedToolchain,
                    Options: ["GenerateSpirvDirectly", "VulkanUseEntryPointName", "--target-env=vulkan1.1", "-O", "--version 430", "--no-es"],
                    Defines.Count: 0,
                    Specializations.Count: 0
                })
                return new(null, GaStartupReason.IncompatibleManifest,
                    "OpenGL requires the verified Slang, SPIRV-Tools and SPIRV-Cross compiler options.");

            if (!device.SupportsCompute || requirements.RequiredFeatures.Count != 0)
                return new(null, GaStartupReason.MissingCapability,
                    "The OpenGL adapter requires OpenGL 4.3 compute with three shader storage bindings.");

            if (requirements.WorkgroupX > device.MaxWorkgroupX || requirements.WorkgroupX > device.MaxWorkgroupInvocations ||
                requirements.SharedMemoryBytes != 0 || requirements.StorageBytes > 3L * device.MaxStorageBufferRange ||
                device.MaxGroupsX == 0 || device.MaxStorageBufferRange < sizeof(uint))
                return new(null, GaStartupReason.ResourceBudgetExceeded, "The kernel exceeds the OpenGL adapter limits.");

            Span<uint> bindings = stackalloc uint[3];
            if (!TryMapBindings(manifest, device.MaxStorageBindings, bindings))
                return new(null, GaStartupReason.IncompatibleManifest,
                    "OpenGL needs one readable and two writable uint32 shader storage buffers.");

            IReadOnlyList<GaSourceSpan> sourceMap;
            try
            {
                sourceMap = artifact.DecodeSourceMap();
            }
            catch (Exception failure) when (failure is InvalidDataException or JsonException)
            {
                return new(null, GaStartupReason.IncompatibleManifest, failure.Message);
            }

            uint workgroup = (uint)requirements.WorkgroupX;
            GaOpenGlNative.Lease handle;
            try
            {
                handle = _api.CreateKernel(_context, index, artifact.Artifact, workgroup, bindings);
            }
            catch (GaOpenGlException failure)
            {
                return new(null, GaStartupReason.PipelineCreationFailed, failure.Message);
            }

            try
            {
                if (!_probed.Contains(index))
                {
                    _api.Probe(_context, index);
                    _probed.Add(index);
                }
                return new(new GaOpenGlKernel(_api, handle, workgroup, device.MaxGroupsX, device.MaxStorageBufferRange,
                    sourceMap, manifest.Contract.TickResourceWords,
                    manifest.Contract.BufferSchema is null ? null : manifest.Contract.BufferRowWords), GaStartupReason.None);
            }
            catch (GaOpenGlException failure)
            {
                handle.Dispose();
                return new(null, GaStartupReason.ProbeFailed, failure.Message);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    private static bool TryMapBindings(GaArtifactManifest manifest, uint maxBindings, Span<uint> slots)
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
            var access = slot == 0 ? GaResourceAccess.Read : GaResourceAccess.ReadWrite;
            if (binding.Namespace != GaBindingNamespace.OpenGlShaderStorage || binding.Space != 0 ||
                binding.Index >= maxBindings || binding.Access != access || binding.Count != 1 ||
                binding.Kind != GaResourceKind.StorageBuffer ||
                binding.Layout is not { Size: sizeof(uint), Alignment: sizeof(uint), ElementStride: sizeof(uint), Fields.Count: 0 })
                return false;
            slots[slot] = (uint)binding.Index;
            used++;
        }
        return used == slots.Length;
    }

    private sealed class Device : IGaDevice
    {
        private readonly GaOpenGlBackend _backend;
        private readonly uint _index;
        private readonly GaOpenGlNative.DeviceProperties _properties;

        internal Device(GaOpenGlBackend backend, uint index, GaOpenGlNative.DeviceProperties properties)
        {
            _backend = backend;
            _index = index;
            _properties = properties;
            Info = new GaDeviceInfo("SKECS.GA WGL", "OpenGL", $"{properties.GlMajor}.{properties.GlMinor}",
                $"{properties.GetVendor()} {properties.GetRenderer()}", properties.GetVersion());
            var portable = new GaDeviceProfile(GaComputeProfiles.Portable32, GaQualification.Experimental, [],
                ToInt(properties.MaxWorkgroupX), 1, 1, ToInt(properties.MaxWorkgroupInvocations), 0,
                3L * properties.MaxStorageBufferRange);
            Profiles = properties.SupportsCompute
                ? Array.AsReadOnly(new[] { portable, portable with { Profile = GaComputeProfiles.WideFixedEmulated } })
                : [];
        }

        public GaDeviceInfo Info { get; }
        public GaArtifactFormat Format => GaArtifactFormat.Glsl;
        public IReadOnlyList<GaDeviceProfile> Profiles { get; }
        public GaPreparation Prepare(GaArtifactBundle artifact) => _backend.Prepare(_index, _properties, artifact);

        private static int ToInt(uint value) => (int)Math.Min(value, (uint)int.MaxValue);
    }
}
