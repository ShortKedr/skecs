using System.Diagnostics;
using System.Text.Json;
using SKECS;
using SKECS.GA;
#if !GA_BROWSER_CONFORMANCE
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Metal;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif

var contract = new GaContractIdentity(ThermalAdapterRowCodec.SystemId, "thermal-inventory/1",
    ThermalAdapterRowCodec.SourceDigest, ThermalAdapterRowCodec.IrDigest, ThermalAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = ThermalAdapterRowCodec.EventCapacity,
    EventTypeCount = ThermalAdapterRowCodec.EventTypeCount,
    ReductionTypeCount = ThermalAdapterRowCodec.ReductionTypeCount,
    EventPayloadWords = ThermalAdapterRowCodec.EventPayloadWords,
    TickResourceType = ThermalAdapterRowCodec.TickResourceType,
    TickResourceWords = ThermalAdapterRowCodec.TickResourceWords
};
Require(contract is { EventCapacity: 3, EventTypeCount: 3, ReductionTypeCount: 1, EventPayloadWords: 9, TickResourceWords: 13 },
    "Unexpected ThermalInventory output or tick resource contract.");

#if GA_BROWSER_CONFORMANCE
var browser = await BrowserConformance.OpenAsync(args, contract, "matrix");
using var backend = browser.Device;
using var startup = browser.Startup;
var artifact = browser.Artifact;
string profile = browser.Profile;
var device = backend;
#else
if (args.Length is 1 or 2 && args[0] == "cpu")
{
    await Exercise(args.ElementAtOrDefault(1) ?? "matrix", null);
    return;
}
if (args.Length is not (4 or 5) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
    !int.TryParse(args[3], out int index) || index < 0)
    throw new ArgumentException("Usage: ThermalInventoryConformance cpu [profile] | <vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native library> <device index> [profile]");

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
    const bool browser = true;
#else
    const bool browser = false;
#endif
    var (scenarios, ticks) = ThermalScenario.SelectCases(profile, singleWorker: browser);
    var coverage = default(ThermalCoverage);
    foreach (var scenario in scenarios)
        coverage += await ThermalScenario.Run(scenario, ticks, startup is not null, browser, Register, Drain, Console.WriteLine);
    ThermalScenario.VerifyCoverage(coverage, startup is not null);
    Console.WriteLine($"ThermalInventory route={(startup is null ? "cpu" : "gpu")} profile={profile} cases={scenarios.Length} " +
        $"ticks={scenarios.Length * ticks} rows={coverage.Rows} transfers={coverage.Transfers} " +
        $"overheated={coverage.Overheated} heatRefusals={coverage.HeatRefusals} heatSpentRaw={coverage.HeatSpent} " +
        $"docked={coverage.Docked} kits={coverage.Kits} enabledKits={coverage.EnabledKits} slots=0x{coverage.Slots:x4} " +
        $"publicEvents={coverage.Rows + coverage.Transfers} deviceRows={coverage.DeviceRows} deviceBatches={coverage.DeviceBatches} " +
        "exactState=passed exactTypedEvents=passed typedReduction=passed");
#if GA_BROWSER_CONFORMANCE
    Console.WriteLine("WebGPU ThermalInventory pendingReadback=passed");
#endif

    void Register(DeterministicWorkload<ThermalFrame> workload, DeterministicReduction<ThermalContribution> counters, Action<GaBatchTimings> observe)
    {
        if (startup is null)
            ThermalAdapter.Register(workload, SystemStage.Update, counters);
        else
            workload.AddBatchSystem(SystemStage.Update, ThermalAdapterRowCodec.CreateQuery(),
                new GaBatchProcessor<ThermalFrame>(startup, contract.SystemId, contract.SourceDigest, contract.IrDigest,
                    contract.ComponentSchemaDigest, ThermalAdapterRowCodec.WordsPerRow, ThermalAdapterRowCodec.Pack<ThermalFrame>,
                    ThermalAdapterRowCodec.Apply<ThermalFrame>, contract.EventCapacity,
                    observe, null, false, [], [], ThermalAdapterRowCodec.TickResource, null, ThermalAdapterRowCodec.CreateEventTypes(counters)));
    }
}

#if GA_BROWSER_CONFORMANCE
static async ValueTask<bool> Drain(DeterministicRunner<ThermalFrame> runner, int budget)
#else
static ValueTask<bool> Drain(DeterministicRunner<ThermalFrame> runner, int budget)
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
        throw new InvalidOperationException("ThermalInventory tick faulted.", runner.Fault);
    if (status != DeterministicTickStatus.Completed)
        throw new TimeoutException("ThermalInventory stayed pending for 20 seconds.");
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
