using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKECS.GA.Backend.WebGpu;

[SupportedOSPlatform("browser")]
public sealed class GaWebGpuBackend : IGaDevice, IDisposable
{
    private const string PinnedToolchain = "Slang WGSL emitter slangc.exe " +
        "sha256:bac1eff312cae0fb5ffb50445215d2774a4875c478b9505303f873b1afa4eaeb, slang.dll " +
        "sha256:dfca2d5eb494fe91e6a650aeaa45000d983dc6801728537bb7201d70bb5d76e6";

    private readonly GaWebGpuProperties _device;
    private Task? _probe;
    private bool _disposed;

    private GaWebGpuBackend(GaWebGpuProperties device)
    {
        _device = device;
        string display = string.Join(" ", new[] { device.Vendor, device.Architecture, device.Renderer }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());
        Info = new GaDeviceInfo("SKECS.GA Browser", "WebGPU", "WGSL", display, "Browser-reported identity");
        var portable = new GaDeviceProfile(GaComputeProfiles.Portable32, GaQualification.Experimental, [],
            device.MaxWorkgroupX, 1, 1, device.MaxWorkgroupInvocations, 0, 3L * device.MaxStorageBufferRange);
        Profiles = Array.AsReadOnly(new[] { portable, portable with { Profile = GaComputeProfiles.WideFixedEmulated } });
    }

    public GaDeviceInfo Info { get; }
    public GaArtifactFormat Format => GaArtifactFormat.Wgsl;
    public IReadOnlyList<GaDeviceProfile> Profiles { get; }

    /// <summary>Opens a device using a host-served copy of ga-webgpu.mjs.</summary>
    public static async ValueTask<GaWebGpuBackend> OpenAsync(string moduleUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleUrl);
        using var module = await JSHost.ImportAsync(GaWebGpuInterop.ModuleName, moduleUrl);
        var json = await GaWebGpuInterop.OpenDevice();
        var device = JsonSerializer.Deserialize(json, GaWebGpuJson.Default.GaWebGpuProperties)
            ?? throw new InvalidDataException("WebGPU did not return a device.");
        if (device is { DeviceHandle: > 0, IsFallbackAdapter: false, MaxGroupsX: > 0, MaxWorkgroupX: > 0,
            MaxWorkgroupInvocations: > 0, MaxStorageBufferRange: >= sizeof(uint), MaxStorageBindings: >= 3 })
            return new GaWebGpuBackend(device);
        GaWebGpuInterop.ReleaseDevice(device.DeviceHandle);
        throw new PlatformNotSupportedException("A hardware WebGPU device with three storage buffers is required.");
    }

    public static async ValueTask<(GaWebGpuBackend? Backend, string? Error)> TryOpenAsync(string moduleUrl)
    {
        try
        {
            return (await OpenAsync(moduleUrl), null);
        }
        catch (Exception failure) when (failure is JSException or PlatformNotSupportedException)
        {
            return (null, failure.Message);
        }
    }

    public GaPreparation Prepare(GaArtifactBundle artifact) =>
        new(null, GaStartupReason.PipelineCreationFailed, "WebGPU requires asynchronous startup.");

    public async ValueTask<GaPreparation> PrepareAsync(GaArtifactBundle artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var manifest = artifact.Manifest;
        var requirements = manifest.Requirements;
        if (manifest.Toolchain is not
            {
                Format: GaArtifactFormat.Wgsl, SlangVersion: "2025.6.1", TargetProfile: "wgsl",
                DownstreamVersion: PinnedToolchain, Options: ["-entry main", "-target wgsl", "storage arrayLength"],
                Defines.Count: 0, Specializations.Count: 0
            } ||
            manifest.EntryPoint != "main" || requirements.WorkgroupY != 1 || requirements.WorkgroupZ != 1 ||
            requirements.ComputeProfile is not (GaComputeProfiles.Portable32 or GaComputeProfiles.WideFixedEmulated))
            return new(null, GaStartupReason.IncompatibleManifest, "WebGPU requires a verified one-dimensional WGSL GA kernel.");
        if (requirements.RequiredFeatures.Count != 0)
            return new(null, GaStartupReason.MissingCapability, "The WebGPU backend supports the portable profile features.");
        if (requirements.WorkgroupX > _device.MaxWorkgroupX || requirements.WorkgroupX > _device.MaxWorkgroupInvocations ||
            requirements.SharedMemoryBytes != 0 || requirements.StorageBytes > 3L * _device.MaxStorageBufferRange)
            return new(null, GaStartupReason.ResourceBudgetExceeded, "The kernel exceeds the WebGPU device limits.");

        var bindings = new int[3];
        if (!TryMapBindings(manifest, bindings))
            return new(null, GaStartupReason.IncompatibleManifest,
                "WebGPU requires one readable and two writable uint32 storage buffers in bind group zero.");
        IReadOnlyList<GaSourceSpan> sourceMap;
        string source;
        try
        {
            sourceMap = artifact.DecodeSourceMap();
            source = new UTF8Encoding(false, true).GetString(artifact.Artifact);
        }
        catch (Exception failure) when (failure is InvalidDataException or JsonException or DecoderFallbackException)
        {
            return new(null, GaStartupReason.IncompatibleManifest, failure.Message);
        }
        string signature = FormattableString.Invariant($"\n@compute\n@workgroup_size({requirements.WorkgroupX}, 1, 1)\nfn main(");
        if (!source.Contains(signature, StringComparison.Ordinal))
            return new(null, GaStartupReason.IncompatibleManifest,
                "The WGSL entry point does not match the manifest workgroup.");

        int handle;
        try
        {
            handle = await GaWebGpuInterop.PrepareKernel(_device.DeviceHandle, source, manifest.EntryPoint,
                requirements.WorkgroupX, bindings[0], bindings[1], bindings[2]);
        }
        catch (JSException failure)
        {
            return new(null, GaStartupReason.PipelineCreationFailed, failure.Message);
        }

        try
        {
            await (_probe ??= ProbeAsync());
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new(new Kernel(handle, _device, manifest, sourceMap), GaStartupReason.None);
        }
        catch (Exception failure) when (failure is JSException or InvalidDataException or ObjectDisposedException)
        {
            GaWebGpuInterop.ReleaseKernel(handle);
            return new(null, GaStartupReason.ProbeFailed, failure.Message);
        }
        catch
        {
            GaWebGpuInterop.ReleaseKernel(handle);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GaWebGpuInterop.ReleaseDevice(_device.DeviceHandle);
    }

    private async Task ProbeAsync()
    {
        const string source = """
            @group(0) @binding(2) var<storage, read> input: array<u32>;
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @group(0) @binding(1) var<storage, read_write> faults: array<u32>;
            @compute @workgroup_size(1)
            fn main() {
                output[0] = input[0] ^ 0x80000000u;
                faults[0] = arrayLength(&input);
            }
            """;
        int kernel = await GaWebGpuInterop.PrepareKernel(_device.DeviceHandle, source, "main", 1, 2, 0, 1);
        try
        {
            uint[] output = new uint[1];
            uint[] faults = new uint[1];
            await GaWebGpuInterop.Dispatch(kernel, 1, [42u, uint.MaxValue], output, faults);
            if (output[0] != 0x8000002Au || faults[0] != 2)
                throw new InvalidDataException("WebGPU failed the packed storage binding and readback probe.");
        }
        finally
        {
            GaWebGpuInterop.ReleaseKernel(kernel);
        }
    }

    private bool TryMapBindings(GaArtifactManifest manifest, int[] slots)
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
            if (binding.Namespace != GaBindingNamespace.WebGpuBinding || binding.Space != 0 ||
                binding.Index >= _device.MaxStorageBindings || binding.Access != access || binding.Count != 1 ||
                binding.Kind != GaResourceKind.StorageBuffer ||
                binding.Layout is not { Size: sizeof(uint), Alignment: sizeof(uint), ElementStride: sizeof(uint), Fields.Count: 0 })
                return false;
            slots[slot] = binding.Index;
            used++;
        }
        return used == slots.Length;
    }

    private sealed class Kernel(int handle, GaWebGpuProperties device, GaArtifactManifest manifest,
        IReadOnlyList<GaSourceSpan> sourceMap)
        : GaPackedKernel((uint)manifest.Requirements.WorkgroupX, device.MaxGroupsX, device.MaxStorageBufferRange,
            sourceMap, manifest.Contract.TickResourceWords,
            manifest.Contract.BufferSchema is null ? null : manifest.Contract.BufferRowWords)
    {
        protected override bool SupportsBlockingDispatch => false;
        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
            throw new NotSupportedException("WebGPU requires asynchronous readback.");
        protected override Task DispatchAsync(uint groups, uint[] input, uint[] output, uint[] faults) =>
            GaWebGpuInterop.Dispatch(handle, checked((int)groups), input, output, faults);
        protected override void Release() => GaWebGpuInterop.ReleaseKernel(handle);
    }
}

internal sealed record GaWebGpuProperties(int DeviceHandle, string Vendor, string Architecture, string Renderer,
    bool IsFallbackAdapter, uint MaxGroupsX, int MaxWorkgroupX, int MaxWorkgroupInvocations,
    uint MaxStorageBufferRange, int MaxStorageBindings);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GaWebGpuProperties))]
internal sealed partial class GaWebGpuJson : JsonSerializerContext;
