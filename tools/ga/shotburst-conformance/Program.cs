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
using ShotBurstFixture;

#if !GA_BROWSER_CONFORMANCE
if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0 ||
    args[4] is not ("smoke" or "matrix" or "short" or "long"))
    throw new ArgumentException("Usage: ShotBurstConformance <vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native bridge> <device index> <smoke|matrix|short|long>");
#endif

var contract = new GaContractIdentity(ShotBurstAdapterRowCodec.SystemId, "shot-burst-capacity/1",
    ShotBurstAdapterRowCodec.SourceDigest, ShotBurstAdapterRowCodec.IrDigest,
    ShotBurstAdapterRowCodec.ComponentSchemaDigest, GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = ShotBurstAdapterRowCodec.EventCapacity,
    UsesCommands = ShotBurstAdapterRowCodec.UsesCommands,
    UsesDomainOrder = ShotBurstAdapterRowCodec.UsesDomainOrder,
    TickResourceType = ShotBurstAdapterRowCodec.TickResourceType,
    TickResourceWords = ShotBurstAdapterRowCodec.TickResourceWords,
    EventTypeCount = ShotBurstAdapterRowCodec.EventTypes.Length,
    EventPayloadWords = ShotBurstAdapterRowCodec.EventPayloadWords
};
Check(contract is { EventCapacity: 2, UsesCommands: false, UsesDomainOrder: true,
    TickResourceWords: 6, EventTypeCount: 1, EventPayloadWords: 7 }, "Unexpected producer contract.");

#if GA_BROWSER_CONFORMANCE
var browser = await BrowserConformance.OpenAsync(args, contract, "smoke");
using var backend = browser.Device;
using var startup = browser.Startup;
var artifact = browser.Artifact;
string profile = browser.Profile;
var device = backend.Info;
string lane = $"WebGPU {device.Device}";
#else
var stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "metal" => ".metallib", "opengl" or "gles" => ".glsl", _ => ".dxil" })),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
var devices = Open(args[0], args[2], out var owner);
using var backend = owner;
Check(deviceIndex < devices.Count, $"Device {deviceIndex} is outside the {devices.Count} enumerated devices.");
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])],
    [devices[deviceIndex]], allowExperimental: true);
var device = devices[deviceIndex].Info;
string profile = args[4];
string lane = $"{args[0]} {device.Device}";
#endif
Check(profile is "smoke" or "matrix" or "short" or "long", "Profile must be smoke, matrix, short, or long.");
Check(startup.Succeeded && startup.Decisions.Single().Route == GaRoute.Gpu,
    string.Join(", ", startup.Decisions.Select(decision => $"{decision.Reason} {decision.Detail}")));
await RunSuite(profile, lane, device, startup, contract, artifact);

static async ValueTask RunSuite(string profile, string lane, GaDeviceInfo device, GaStartupResult startup,
    GaContractIdentity contract, GaArtifactCandidate artifact)
{
    var manifest = artifact.Bundle!.Manifest;
#if GA_BROWSER_CONFORMANCE
    const bool singleWorker = true;
#else
    const bool singleWorker = false;
#endif
    var (cases, ticks) = ShotBurstScenario.SelectCases(profile, singleWorker);
    Console.WriteLine($"profile={profile} device={device.Device} api={device.Api} apiVersion={device.ApiVersion} driver={device.Driver} " +
        $"cases={cases.Length} ticksPerCase={ticks} workers={string.Join(',', cases.Select(item => item.Workers).Distinct().Order())} " +
        $"workgroup={manifest.Requirements.WorkgroupX} source={contract.SourceDigest} ir={contract.IrDigest} " +
        $"numericVersion={contract.NumericVersion} subsetVersion={contract.SubsetVersion} irVersion={contract.IrVersion} " +
        $"artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} contentId={startup.Decisions.Single().ArtifactId}");
    Console.WriteLine($"toolchain={JsonSerializer.Serialize(manifest.Toolchain)}");
    var timer = Stopwatch.StartNew();
    var total = new Totals(0, 0, 0, 0, 0);
    long deviceBatches = 0;
    long deviceRows = 0;
    foreach (var scenario in cases)
    {
        var (counters, batches, rows) = await ShotBurstScenario.RunCase(scenario, ticks, useGpu: true,
            group => ShotBurstAdapter.Register(group), RegisterGpu, Drain, Console.WriteLine,
            mismatch => Console.Error.WriteLine(JsonSerializer.Serialize(mismatch, new JsonSerializerOptions { IncludeFields = true })));
        total = total.With(counters);
        deviceBatches += batches;
        deviceRows += rows;
    }
    ShotBurstScenario.VerifyCoverage(total);
    Console.WriteLine($"{lane}: profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
        $"sizeBoundaries={string.Join(',', cases.Select(item => item.Rows).Distinct().Order())} " +
        $"seeds={string.Join(',', cases.Select(item => item.Seed).Distinct().Order())} " +
        $"requests={total.Requests} decisions={total.Accepted + total.Rejected} events={2 * total.Requests} " +
        $"accepted={total.Accepted} capacityRejected={total.Rejected} heatRejected={total.HeatRejected} zeroReseeds={total.ZeroReseeds} " +
        $"deviceBatches={deviceBatches} deviceRows={deviceRows} elapsedSeconds={timer.Elapsed.TotalSeconds:F2} " +
        "domainAndOrdinals=passed fixedAndRng=passed ticketIds=passed reusedSourceGeneration=passed");

    void RegisterGpu(DeterministicCommitGroup<ShotFrame> group, Action<GaBatchTimings> observe)
    {
        group.AddBatchSystem(ShotBurstAdapterRowCodec.CreateQuery(),
            new GaBatchProcessor<ShotFrame>(startup, contract.SystemId, contract.SourceDigest,
                contract.IrDigest, contract.ComponentSchemaDigest, ShotBurstAdapterRowCodec.WordsPerRow,
                ShotBurstAdapterRowCodec.Pack<ShotFrame>, ShotBurstAdapterRowCodec.Apply<ShotFrame>, contract.EventCapacity,
                observe, null, false, [], [], ShotBurstAdapterRowCodec.TickResource, null, ShotBurstAdapterRowCodec.EventTypes));
    }
}

#if GA_BROWSER_CONFORMANCE
static async ValueTask Drain(DeterministicRunner<ShotFrame> runner, int budget, bool deviceWork)
#else
static ValueTask Drain(DeterministicRunner<ShotFrame> runner, int budget, bool deviceWork)
#endif
{
    long started = Stopwatch.GetTimestamp();
#if !GA_BROWSER_CONFORMANCE
    var spin = new SpinWait();
#endif
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed.TotalSeconds >= 20)
            throw new TimeoutException("The runner stayed pending for 20 seconds.");
#if GA_BROWSER_CONFORMANCE
        if (deviceWork)
            await Task.Delay(1);
#else
        if (elapsed.TotalMilliseconds < 2)
            spin.SpinOnce(sleep1Threshold: -1);
        else
            Thread.Sleep(1);
#endif
        status = runner.Advance(budget);
    }
    if (status != DeterministicTickStatus.Completed)
        throw new InvalidOperationException($"Expected Completed, got {status}.", runner.Fault);
#if !GA_BROWSER_CONFORMANCE
    return ValueTask.CompletedTask;
#endif
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

#if !GA_BROWSER_CONFORMANCE
static IReadOnlyList<IGaDevice> Open(string backend, string library, out IDisposable owner)
{
    if (backend == "vulkan")
    {
        var vk = GaVulkanBackend.Open(library);
        owner = vk;
        return vk.Devices;
    }
    if (backend == "d3d11")
    {
        var d3d11 = GaD3D11Backend.Open(library);
        owner = d3d11;
        return d3d11.Devices;
    }
    if (backend is "opengl" or "gles")
    {
        var gl = backend == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(library)
            : GaOpenGlBackend.Open(library);
        owner = gl;
        return gl.Devices;
    }
    if (backend == "metal")
    {
        var metal = GaMetalBackend.Open(library);
        owner = metal;
        return metal.Devices;
    }
    var dx = GaD3D12Backend.Open(library);
    owner = dx;
    return dx.Devices;
}
#endif
