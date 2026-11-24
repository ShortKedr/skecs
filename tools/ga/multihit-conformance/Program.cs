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
using MultiHitFixture;

#if !GA_BROWSER_CONFORMANCE
string profile = args.Length is 2 or 5 ? args[^1] : "smoke";
if (profile is not ("smoke" or "matrix" or "short" or "long"))
    throw new ArgumentException("Profile must be smoke, matrix, short, or long.");
if (args.Length is 1 or 2 && args[0] == "cpu")
{
    await RunSuite("cpu", profile, null, null);
    return;
}
if (args.Length is not (4 or 5) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Usage: MultiHitConformance (cpu | <vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native bridge> <device index>) [profile]");
#endif

var contract = new GaContractIdentity(MultiHitAdapterRowCodec.SystemId, "multi-hit-damage/1",
    MultiHitAdapterRowCodec.SourceDigest, MultiHitAdapterRowCodec.IrDigest,
    MultiHitAdapterRowCodec.ComponentSchemaDigest, GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = MultiHitAdapterRowCodec.EventCapacity,
    UsesCommands = MultiHitAdapterRowCodec.UsesCommands,
    UsesDomainOrder = MultiHitAdapterRowCodec.UsesDomainOrder,
    TickResourceType = MultiHitAdapterRowCodec.TickResourceType,
    TickResourceWords = MultiHitAdapterRowCodec.TickResourceWords,
    EventTypeCount = MultiHitAdapterRowCodec.EventTypes.Length,
    EventPayloadWords = MultiHitAdapterRowCodec.EventPayloadWords
};
Check(contract is { EventCapacity: 1, UsesCommands: false, UsesDomainOrder: true, TickResourceWords: 1,
    EventTypeCount: 1, EventPayloadWords: 9 }, "Unexpected hit collection contract.");

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
Check(deviceIndex < devices.Count, $"Device index {deviceIndex} is outside the {devices.Count} enumerated devices.");
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])],
    [devices[deviceIndex]], allowExperimental: true);
var device = devices[deviceIndex].Info;
string lane = $"{args[0]} {device.Device}";
#endif
Check(profile is "smoke" or "matrix" or "short" or "long", "Profile must be smoke, matrix, short, or long.");
Check(startup.Succeeded && startup.Decisions.Single().Route == GaRoute.Gpu,
    string.Join(", ", startup.Decisions.Select(decision => $"{decision.Reason} {decision.Detail}")));
var manifest = artifact.Bundle!.Manifest;
Console.WriteLine($"profile={profile} device={device.Device} api={device.Api} apiVersion={device.ApiVersion} driver={device.Driver} " +
    $"workgroup={manifest.Requirements.WorkgroupX} source={contract.SourceDigest} ir={contract.IrDigest} " +
    $"numericVersion={contract.NumericVersion} subsetVersion={contract.SubsetVersion} irVersion={contract.IrVersion} " +
    $"artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} contentId={startup.Decisions.Single().ArtifactId}");
Console.WriteLine($"toolchain={JsonSerializer.Serialize(manifest.Toolchain)}");
await RunSuite(lane, profile, startup, contract);

static ValueTask RunSuite(string lane, string profile, GaStartupResult? startup, GaContractIdentity? contract)
{
#if GA_BROWSER_CONFORMANCE
    const bool singleWorker = true;
#else
    const bool singleWorker = false;
#endif
    return MultiHitScenario.RunSuite(lane, profile, singleWorker, startup is not null,
        (group, _) => MultiHitAdapter.Register(group), startup is null ? null : RegisterGpu,
        Drain, Console.WriteLine,
        mismatch => Console.Error.WriteLine(JsonSerializer.Serialize(mismatch, new JsonSerializerOptions { IncludeFields = true })));

    void RegisterGpu(DeterministicCommitGroup<DamageFrame> group, Action<GaBatchTimings> observe)
    {
        group.AddBatchSystem(MultiHitAdapterRowCodec.CreateQuery(), new GaBatchProcessor<DamageFrame>(
            startup!, contract!.SystemId, contract.SourceDigest, contract.IrDigest, contract.ComponentSchemaDigest,
            MultiHitAdapterRowCodec.WordsPerRow, MultiHitAdapterRowCodec.Pack<DamageFrame>,
            MultiHitAdapterRowCodec.Apply<DamageFrame>, contract.EventCapacity, observe, null, false, [], [],
            MultiHitAdapterRowCodec.TickResource, null, MultiHitAdapterRowCodec.EventTypes));
    }
}

#if GA_BROWSER_CONFORMANCE
static async ValueTask<DeterministicTickStatus> Drain(DeterministicRunner<DamageFrame> runner, int budget, bool deviceWork)
{
    long started = Stopwatch.GetTimestamp();
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20))
    {
        if (deviceWork)
            await Task.Delay(1);
        status = runner.Advance(budget);
    }
    return status;
}
#else
static ValueTask<DeterministicTickStatus> Drain(DeterministicRunner<DamageFrame> runner, int budget, bool deviceWork)
{
    var status = DeterministicTickStatus.Pending;
    SpinWait.SpinUntil(() => (status = runner.Advance(budget)) != DeterministicTickStatus.Pending,
        TimeSpan.FromSeconds(20));
    return ValueTask.FromResult(status);
}

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

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
