using System.Diagnostics;
using System.Text.Json;
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
using SKECS.GA.Backend.Metal;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif

var contract = new GaContractIdentity(SpatialAdapterRowCodec.SystemId, "spatial-parent/1",
    SpatialAdapterRowCodec.SourceDigest, SpatialAdapterRowCodec.IrDigest, SpatialAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    UsesSnapshot = true,
    SnapshotSchema = SpatialAdapterRowCodec.SnapshotSchema,
    BufferSchema = SpatialAdapterRowCodec.BufferSchema,
    BufferRowWords = SpatialAdapterRowCodec.BufferRowWords,
    EventCapacity = SpatialAdapterRowCodec.EventCapacity,
    EventTypeCount = SpatialAdapterRowCodec.EventTypeCount,
    EventPayloadWords = SpatialAdapterRowCodec.EventPayloadWords
};
Require(contract is { SnapshotSchema: "bbff", BufferRowWords: 12, EventCapacity: 1, EventTypeCount: 1, EventPayloadWords: 19 },
    "Unexpected SpatialParent snapshot, buffer, or event contract.");

#if GA_BROWSER_CONFORMANCE
if (!OperatingSystem.IsBrowser())
    throw new PlatformNotSupportedException();
var host = WebAssemblyHostBuilder.CreateDefault(args).Build();
var navigation = host.Services.GetRequiredService<NavigationManager>();
var site = new Uri(navigation.BaseUri);
string query = new Uri(navigation.Uri).Query;
string profile = query.Length == 0 ? "matrix" : query[1..];
using var http = new HttpClient { BaseAddress = site };
string stem = "bundle/" + contract.SystemId;
var manifestBytes = await http.GetByteArrayAsync(stem + ".manifest.json");
var contentId = await http.GetStringAsync(stem + ".content-id");
var shaderBytes = await http.GetByteArrayAsync(stem + ".wgsl");
var layoutBytes = await http.GetByteArrayAsync(stem + ".layout");
var sourceMapBytes = await http.GetByteArrayAsync(stem + ".source-map.json");
var artifact = GaArtifactCandidate.Load(manifestBytes, contentId.Trim(), shaderBytes, layoutBytes, sourceMapBytes);
var opened = await GaWebGpuBackend.TryOpenAsync(new Uri(site, "ga-webgpu.mjs").AbsoluteUri);
using var backend = opened.Backend ?? throw new InvalidOperationException(opened.Error);
var device = backend;
using var startup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [new(contract, [artifact])], [device], allowExperimental: true);
#else
if (args.Length is 1 or 2 && args[0] == "cpu")
{
    await Exercise(args.ElementAtOrDefault(1) ?? "matrix", null);
    return;
}
if (args.Length is not (4 or 5) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
    !int.TryParse(args[3], out int index) || index < 0)
    throw new ArgumentException("Usage: SpatialParentConformance cpu [profile] | <vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native library> <device index> [profile]");

var stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "metal" => ".metallib", "opengl" or "gles" => ".glsl", _ => ".dxil" })),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
var devices = Open(args[0], args[2], out var owner);
using var native = owner;
Require(index < devices.Count, $"Device index {index} exceeds available devices {devices.Count}.");
var device = devices[index];
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [device], allowExperimental: true);
string profile = args.ElementAtOrDefault(4) ?? "matrix";
#endif
var route = startup.Decisions.Single();
Require(startup.Succeeded && route.Route == GaRoute.Gpu, $"GPU startup failed: {route.Reason} {route.Detail}");
var manifest = artifact.Bundle!.Manifest;
Console.WriteLine($"device={device.Info.Device} api={device.Info.Api} version={device.Info.ApiVersion} driver={device.Info.Driver} " +
    $"source={contract.SourceDigest} ir={contract.IrDigest} irVersion={contract.IrVersion} subsetVersion={contract.SubsetVersion} " +
    $"numericVersion={contract.NumericVersion} artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} " +
    $"contentId={route.ArtifactId} workgroup={manifest.Requirements.WorkgroupX}");
Console.WriteLine($"toolchain={JsonSerializer.Serialize(manifest.Toolchain)}");
await Exercise(profile, startup);

async Task Exercise(string profile, GaStartupResult? startup)
{
#if GA_BROWSER_CONFORMANCE
    const bool requireYield = true;
#else
    const bool requireYield = false;
#endif
    var (scenarios, ticks) = SpatialScenario.SelectCases(profile);
    var totals = default(SpatialCoverage);
    foreach (var scenario in scenarios)
        totals += await SpatialScenario.Run(scenario, ticks, startup is not null, requireYield,
            SpatialAdapterRowCodec.Buffer, Register, Drain, Console.WriteLine);
    SpatialScenario.VerifyCoverage(totals, startup is not null);
    Console.WriteLine($"SpatialParent route={(startup is null ? "cpu" : "gpu")} profile={profile} cases={scenarios.Length} " +
        $"ticks={ticks * scenarios.Length} queries={totals.Queries} candidateOccurrences={totals.Candidates} duplicates={totals.Duplicates} " +
        $"exactUnique={totals.Exact} staleTargets={totals.Stale} deviceRows={(startup is null ? 0 : totals.Queries)} " +
        $"deviceBatches={totals.Batches} witnesses={totals.Witnesses} " +
        "exactState=passed exactTypedEvents=passed bruteForceCoverage=passed sealAddClear=passed");
#if GA_BROWSER_CONFORMANCE
    Console.WriteLine("WebGPU SpatialParent pendingReadback=passed");
#endif

    void Register(DeterministicWorkload<byte> workload, GaTickBuffer buffer, GaTickSnapshot snapshot, Action<GaBatchTimings> observe)
    {
        if (startup is null)
            SpatialAdapter.Register(workload, SystemStage.Update, buffer, snapshot);
        else
            workload.AddBatchSystem(SystemStage.Update, SpatialAdapterRowCodec.CreateQuery(),
                new GaBatchProcessor<byte>(startup, contract.SystemId, contract.SourceDigest, contract.IrDigest,
                    contract.ComponentSchemaDigest, SpatialAdapterRowCodec.WordsPerRow, SpatialAdapterRowCodec.Pack<byte>,
                    SpatialAdapterRowCodec.Apply<byte>, contract.EventCapacity, observe, snapshot,
                    false, [], [], default, buffer, SpatialAdapterRowCodec.EventTypes));
    }
}

#if GA_BROWSER_CONFORMANCE
static async ValueTask<bool> Drain(DeterministicRunner<byte> runner, int budget)
#else
static ValueTask<bool> Drain(DeterministicRunner<byte> runner, int budget)
#endif
{
    var timer = Stopwatch.StartNew();
    bool pending = false;
#if !GA_BROWSER_CONFORMANCE
    var spin = new SpinWait();
#endif
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(20))
    {
        pending = true;
#if GA_BROWSER_CONFORMANCE
        await Task.Delay(1);
#else
        if (timer.Elapsed.TotalMilliseconds < 2)
            spin.SpinOnce(sleep1Threshold: -1);
        else
            Thread.Sleep(1);
#endif
        status = runner.Advance(budget);
    }
    if (status == DeterministicTickStatus.Faulted)
        throw new InvalidOperationException("SpatialParent tick faulted.", runner.Fault);
    if (status != DeterministicTickStatus.Completed)
        throw new TimeoutException("SpatialParent stayed pending for 20 seconds.");
#if GA_BROWSER_CONFORMANCE
    return pending;
#else
    return new ValueTask<bool>(pending);
#endif
}

#if !GA_BROWSER_CONFORMANCE
static IReadOnlyList<IGaDevice> Open(string backend, string path, out IDisposable owner)
{
    if (backend == "vulkan")
    {
        var vk = GaVulkanBackend.Open(path);
        owner = vk;
        return vk.Devices;
    }
    if (backend == "d3d11")
    {
        var d3d11 = GaD3D11Backend.Open(path);
        owner = d3d11;
        return d3d11.Devices;
    }
    if (backend is "opengl" or "gles")
    {
        var gl = backend == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(path)
            : GaOpenGlBackend.Open(path);
        owner = gl;
        return gl.Devices;
    }
    if (backend == "metal")
    {
        var metal = GaMetalBackend.Open(path);
        owner = metal;
        return metal.Devices;
    }
    var dx = GaD3D12Backend.Open(path);
    owner = dx;
    return dx.Devices;
}
#endif

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
}
