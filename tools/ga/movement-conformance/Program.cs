using SKECS;
using SKECS.GA;
#if GA_BROWSER_CONFORMANCE
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SKECS.GA.Backend.WebGpu;
#else
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif
using SKECS.Numerics;
using static MovementFixture;

#if !GA_BROWSER_CONFORMANCE
if (args.Length > 0 && args[0] == "measure")
{
    await MovementMeasurements.Run(args);
    return;
}

if (args is ["cpu", var cpuProfile])
{
    Console.WriteLine($"source={MovementAdapterRowCodec.SourceDigest} ir={MovementAdapterRowCodec.IrDigest} " +
        $"numeric={GaProgram.NumericVersion} subset={GaProgram.SubsetVersion} irVersion={GaProgram.IrVersion}");
    await Run(cpuProfile, "generated CPU", null, null);
    return;
}

if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Use 'cpu <profile>' or '<vulkan|d3d12|d3d11|opengl|gles> <bundle> <native bridge> <device index> <profile>'.");
#endif

var contract = CreateContract();
#if GA_BROWSER_CONFORMANCE
if (!OperatingSystem.IsBrowser())
    throw new PlatformNotSupportedException();
var host = WebAssemblyHostBuilder.CreateDefault(args).Build();
var navigation = host.Services.GetRequiredService<NavigationManager>();
var site = new Uri(navigation.BaseUri);
string query = new Uri(navigation.Uri).Query;
string profile = query switch { "?short" => "short", "?long" => "long", _ => "boundaries" };
using var http = new HttpClient { BaseAddress = site };
string stem = "bundle/" + contract.SystemId;
var manifestBytes = await http.GetByteArrayAsync(stem + ".manifest.json");
var contentId = await http.GetStringAsync(stem + ".content-id");
var shaderBytes = await http.GetByteArrayAsync(stem + ".wgsl");
var layoutBytes = await http.GetByteArrayAsync(stem + ".layout");
var sourceMapBytes = await http.GetByteArrayAsync(stem + ".source-map.json");
var artifact = GaArtifactCandidate.Load(manifestBytes, contentId.Trim(), shaderBytes, layoutBytes, sourceMapBytes);
var request = new GaSystemRequest(contract, [artifact]);
var opened = await GaWebGpuBackend.TryOpenAsync(new Uri(site, "ga-webgpu.mjs").AbsoluteUri);
using var backend = opened.Backend;
if (query == "?fallback")
{
    Check(backend is null && opened.Error is { Length: > 0 }, "The fallback fixture must disable browser WebGPU.");
    using var unavailable = await GaStartup.SelectAsync(GaPolicy.Auto, [request], [], allowExperimental: true);
    Check(unavailable.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.NoComputeApi },
        "WebGPU unavailability did not select CPU fallback.");
    await Run(profile, "WebGPU unavailable CPU fallback", null, null);
    return;
}
if (backend is null)
    throw new InvalidOperationException(opened.Error);
var device = backend.Info;
using var cpuPolicy = await GaStartup.SelectAsync(GaPolicy.CpuOnly, [request], [backend]);
using var autoPolicy = await GaStartup.SelectAsync(GaPolicy.Auto, [request], [backend]);
using var missingDevice = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], []);
Check(cpuPolicy.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.CpuOnlyPolicy } &&
    autoPolicy.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.UnqualifiedConfiguration } &&
    missingDevice.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.NoComputeApi },
    "WebGPU startup policy mismatch.");
var originalManifest = artifact.Bundle?.Manifest ?? throw new InvalidDataException(artifact.Error);
var wrongGroup = originalManifest with { Requirements = originalManifest.Requirements with { WorkgroupX = 32 } };
var incompatibleArtifact = GaArtifactCandidate.Load(wrongGroup.GetCanonicalBytes(), wrongGroup.GetContentId(),
    shaderBytes, layoutBytes, sourceMapBytes);
Check(incompatibleArtifact.Bundle is not null, "The incompatible-workgroup fixture is invalid.");
using var fallback = await GaStartup.SelectAsync(GaPolicy.Auto, [new(contract, [incompatibleArtifact])],
    [backend], allowExperimental: true);
Check(fallback.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.IncompatibleManifest },
    "WebGPU accepted a workgroup that disagrees with the WGSL entry point.");
using var startup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], [backend], allowExperimental: true);
#else
string profile = args[4];
string stem = Path.Combine(args[1], contract.SystemId);
string binaryExtension = args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" or "gles" => ".glsl", _ => ".dxil" };
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + binaryExtension),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
using IDisposable backend = Open(args[0], args[2], out var devices);
Check(deviceIndex < devices.Count, $"Only {devices.Count} devices are available.");
if (args[0] is "d3d11" or "opengl" or "gles")
    VerifyStartup(contract, artifact, devices[deviceIndex]);
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [devices[deviceIndex]], allowExperimental: true);
var device = devices[deviceIndex].Info;
#endif
var decision = startup.Decisions.Single();
Check(startup.Succeeded && decision.Route == GaRoute.Gpu, $"{decision.Reason} {decision.Detail}");
var manifest = artifact.Bundle!.Manifest;
Console.WriteLine($"api={device.Api} apiVersion={device.ApiVersion} device={device.Device} driver={device.Driver} " +
    $"source={contract.SourceDigest} ir={contract.IrDigest} numeric={contract.NumericVersion} subset={contract.SubsetVersion} " +
    $"irVersion={contract.IrVersion} artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} " +
    $"contentId={decision.ArtifactId} workgroup={manifest.Requirements.WorkgroupX} slang={manifest.Toolchain.SlangVersion} " +
    $"target={manifest.Toolchain.TargetProfile} downstream={manifest.Toolchain.DownstreamVersion}");
#if GA_BROWSER_CONFORMANCE
using (var abortedStartup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], [backend], allowExperimental: true))
    await VerifyPendingAbort(abortedStartup, contract);
#endif
await Run(profile, $"{device.Api} {device.Device}", startup, contract);

#if GA_BROWSER_CONFORMANCE
static async Task VerifyPendingAbort(GaStartupResult startup, GaContractIdentity contract)
{
    await using var kernel = (GaPackedKernel)startup.GetPreparedKernel(contract.SystemId);
    var oracle = new MovementOracle(new Scenario(65, 27, 127, 64, 1, 1, 1), new Counters());
    using var world = new World(new WorldOptions(64));
    Populate(world, oracle);
    long appliedRows = 0;
    var workload = new DeterministicWorkload<MovementFrame>();
    RegisterGpu(workload, startup, contract, timing => appliedRows += timing.Rows);
    using var runner = new DeterministicRunner<MovementFrame>(world, workload);

    var frame = new MovementFrame { DeltaTime = Fixed.FromRaw(MovementOracle.StepAt(1)) };
    runner.BeginTick(1, frame, frame.DeltaTime);
    Check(runner.Advance(1) == DeterministicTickStatus.Pending, "Browser readback did not yield.");
    runner.Abort();
    Check(runner.Status == DeterministicTickStatus.Faulted && runner.Fault is OperationCanceledException &&
        runner.LastCompleted is null, "An aborted WebGPU tick was published.");
    runner.DiscardWorld();
    await kernel.DisposeAsync();
    Check(appliedRows == 0 && runner.LastCompleted is null, "A discarded WebGPU submission applied rows.");
    Console.WriteLine("WebGPU pending readback, abort and asynchronous release passed.");
}
#endif

#if !GA_BROWSER_CONFORMANCE
static void VerifyStartup(GaContractIdentity contract, GaArtifactCandidate artifact, IGaDevice device)
{
    string api = device.Info.Api;
    var bundle = artifact.Bundle ?? throw new InvalidOperationException(artifact.Error);
    var request = new GaSystemRequest(contract, [artifact]);
    using var cpu = GaStartup.Select(GaPolicy.CpuOnly, [request], [device]);
    using var unqualified = GaStartup.Select(GaPolicy.Auto, [request], [device]);
    using var absent = GaStartup.Select(GaPolicy.RequireGpu, [request], []);
    Check(cpu.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.CpuOnlyPolicy },
        $"{api} CpuOnly did not select the CPU.");
    Check(unqualified.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.UnqualifiedConfiguration },
        $"{api} Auto selected an Experimental route without opt-in.");
    Check(absent.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.NoComputeApi },
        $"{api} RequireGpu accepted a missing device.");

    string[] unverifiedOptions = device.Format == GaArtifactFormat.Dxbc
        ? ["Slang downstream FXC"]
        : bundle.Manifest.Toolchain.Options.Skip(1).ToArray();
    var unverifiedCompiler = bundle.Manifest with
    {
        Toolchain = bundle.Manifest.Toolchain with { Options = unverifiedOptions }
    };
    var unverifiedCandidate = GaArtifactCandidate.Load(unverifiedCompiler.GetCanonicalBytes(), unverifiedCompiler.GetContentId(),
        bundle.Artifact, bundle.Reflection, bundle.SourceMap);
    Check(unverifiedCandidate.Bundle is not null, $"Invalid {api} compiler-options test fixture: {unverifiedCandidate.Error}");
    using var unverified = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [unverifiedCandidate])],
        [device], allowExperimental: true);
    Check(unverified.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.IncompatibleManifest },
        $"{api} accepted unverified compiler options.");

    var wrongGroup = bundle.Manifest with
    {
        Requirements = bundle.Manifest.Requirements with
        {
            WorkgroupX = bundle.Manifest.Requirements.WorkgroupX == 32 ? 64 : 32
        }
    };
    var candidate = GaArtifactCandidate.Load(wrongGroup.GetCanonicalBytes(), wrongGroup.GetContentId(),
        bundle.Artifact, bundle.Reflection, bundle.SourceMap);
    Check(candidate.Bundle is not null, $"Invalid {api} workgroup test fixture: {candidate.Error}");
    using var rejected = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [candidate])], [device], allowExperimental: true);
    using var fallback = GaStartup.Select(GaPolicy.Auto, [new(contract, [candidate])], [device], allowExperimental: true);
    Check(rejected.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.PipelineCreationFailed },
        $"{api} RequireGpu accepted a workgroup that disagrees with the shader.");
    Check(fallback.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.PipelineCreationFailed },
        $"{api} Auto did not fall back after rejecting a mismatched workgroup.");
    Console.WriteLine($"{api} startup policies, compiler options, missing device and workgroup mismatch passed.");
}
#endif

#if !GA_BROWSER_CONFORMANCE
static IDisposable Open(string target, string library, out IReadOnlyList<IGaDevice> devices)
{
    if (target == "vulkan")
    {
        var backend = GaVulkanBackend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    if (target == "d3d11")
    {
        var backend = GaD3D11Backend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    if (target is "opengl" or "gles")
    {
        var backend = target == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(library)
            : GaOpenGlBackend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    var d3d12 = GaD3D12Backend.Open(library);
    devices = d3d12.Devices;
    return d3d12;
}
#endif
