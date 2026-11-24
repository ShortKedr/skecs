using System.Diagnostics;
using System.Runtime.CompilerServices;
using SKECS;
using SKECS.GA;
#if !GA_BROWSER_CONFORMANCE
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Metal;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif
using CarrierCapacityFixture;

#if !GA_BROWSER_CONFORMANCE
if (args is ["cpu", var cpuProfile])
{
    await Qualify(cpuProfile, null, null, null);
    return;
}

if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Use 'cpu <profile>' or '<vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native bridge> <device index> <profile>'.");
#endif

var contract = new GaContractIdentity(CarrierAdapterRowCodec.SystemId, "carrier-capacity/1",
    CarrierAdapterRowCodec.SourceDigest, CarrierAdapterRowCodec.IrDigest, CarrierAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = CarrierAdapterRowCodec.EventCapacity,
    UsesCommands = CarrierAdapterRowCodec.UsesCommands,
    UsesDomainOrder = CarrierAdapterRowCodec.UsesDomainOrder,
    TickResourceType = CarrierAdapterRowCodec.TickResourceType,
    TickResourceWords = CarrierAdapterRowCodec.TickResourceWords,
    EventTypeCount = CarrierAdapterRowCodec.EventTypes.Length,
    EventPayloadWords = CarrierAdapterRowCodec.EventPayloadWords
};
Require(contract is { EventCapacity: 1, UsesCommands: false, UsesDomainOrder: true, TickResourceWords: 1,
    EventTypeCount: 1, EventPayloadWords: 8 }, "Unexpected carrier kernel contract.");
#if GA_BROWSER_CONFORMANCE
var browser = await BrowserConformance.OpenAsync(args, contract, "boundaries");
using var backend = browser.Device;
using var startup = browser.Startup;
var artifact = browser.Artifact;
string profile = browser.Profile;
var device = backend.Info;
#else
string stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "metal" => ".metallib", "opengl" or "gles" => ".glsl", _ => ".dxil" })),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
using IDisposable backend = Open(args[0], args[2], out var devices);
Require(deviceIndex < devices.Count, $"Only {devices.Count} devices are available.");
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [devices[deviceIndex]], allowExperimental: true);
var device = devices[deviceIndex].Info;
string profile = args[4];
#endif
var selection = startup.Decisions.Single();
Require(startup.Succeeded && selection.Route == GaRoute.Gpu, $"{selection.Reason} {selection.Detail}");
var manifest = artifact.Bundle!.Manifest;
Console.WriteLine($"api={device.Api} apiVersion={device.ApiVersion} device={device.Device} driver={device.Driver} " +
    $"source={contract.SourceDigest} ir={contract.IrDigest} subset={contract.SubsetVersion} numeric={contract.NumericVersion} " +
    $"irVersion={contract.IrVersion} artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} " +
    $"contentId={selection.ArtifactId} workgroup={manifest.Requirements.WorkgroupX}");
Console.WriteLine("GPU stage=CarrierOrders; declared common CPU stages=Build,ObserveCommittedShips,Retire");
await Qualify(profile, startup, contract, device);

static async ValueTask Qualify(string profile, GaStartupResult? startup, GaContractIdentity? contract, GaDeviceInfo? device)
{
#if GA_BROWSER_CONFORMANCE
    const bool browser = true;
#else
    const bool browser = false;
#endif
    string lane = device == null ? "generated CPU" : $"{device.Api} {device.Device}";
    await CarrierChecks.Run(profile, lane, singleWorker: browser, useGpu: startup is not null, requireYield: browser,
        (group, _) => CarrierAdapter.Register(group), startup is null ? null : RegisterGpu, RunToCompletion, Console.WriteLine);
#if GA_BROWSER_CONFORMANCE
    Console.WriteLine("WebGPU CarrierCapacity pendingReadback=passed");
#endif

    void RegisterGpu(DeterministicCommitGroup<CarrierFrame> group, Action<GaBatchTimings> observe)
    {
        group.AddBatchSystem(CarrierAdapterRowCodec.CreateQuery(), new GaBatchProcessor<CarrierFrame>(
            startup!, contract!.SystemId, contract.SourceDigest, contract.IrDigest, contract.ComponentSchemaDigest,
            CarrierAdapterRowCodec.WordsPerRow, CarrierAdapterRowCodec.Pack<CarrierFrame>, CarrierAdapterRowCodec.Apply<CarrierFrame>,
            contract.EventCapacity, observe, null, false, [], [], CarrierAdapterRowCodec.TickResource, null, CarrierAdapterRowCodec.EventTypes));
    }
}

#if GA_BROWSER_CONFORMANCE
static async ValueTask<bool> RunToCompletion(DeterministicRunner<CarrierFrame> runner, int budget)
{
    var timer = Stopwatch.StartNew();
    bool pending = false;
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(20))
    {
        pending = true;
        await Task.Delay(1);
        status = runner.Advance(budget);
    }
    Require(status == DeterministicTickStatus.Completed, $"Runner {status}: {runner.Fault}");
    return pending;
}
#else
static ValueTask<bool> RunToCompletion(DeterministicRunner<CarrierFrame> runner, int budget)
{
    var status = DeterministicTickStatus.Pending;
    bool finished = SpinWait.SpinUntil(
        () => (status = runner.Advance(budget)) != DeterministicTickStatus.Pending, TimeSpan.FromSeconds(20));
    Require(finished && status == DeterministicTickStatus.Completed, $"Runner {status}: {runner.Fault}");
    return new ValueTask<bool>(false);
}
#endif

#if !GA_BROWSER_CONFORMANCE
static IDisposable Open(string api, string library, out IReadOnlyList<IGaDevice> devices)
{
    if (api == "vulkan")
    {
        var backend = GaVulkanBackend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    if (api == "d3d11")
    {
        var d3d11 = GaD3D11Backend.Open(library);
        devices = d3d11.Devices;
        return d3d11;
    }
    if (api is "opengl" or "gles")
    {
        var gl = api == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(library)
            : GaOpenGlBackend.Open(library);
        devices = gl.Devices;
        return gl;
    }
    if (api == "metal")
    {
        var metal = GaMetalBackend.Open(library);
        devices = metal.Devices;
        return metal;
    }
    var d3d12 = GaD3D12Backend.Open(library);
    devices = d3d12.Devices;
    return d3d12;
}
#endif

static void Require(bool condition, string context, [CallerArgumentExpression(nameof(condition))] string? expression = null)
{
    if (!condition)
        throw new InvalidOperationException($"{context} [{expression}]");
}
