using System.Text.Json;
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

var contract = new GaContractIdentity(TargetAdapterRowCodec.SystemId, "target-read/1",
    TargetAdapterRowCodec.SourceDigest, TargetAdapterRowCodec.IrDigest, TargetAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    UsesSnapshot = true,
    SnapshotSchema = TargetAdapterRowCodec.SnapshotSchema,
    EventCapacity = TargetAdapterRowCodec.EventCapacity,
    EventTypeCount = TargetAdapterRowCodec.EventTypes.Length,
    EventPayloadWords = TargetAdapterRowCodec.EventPayloadWords
};
Require(contract is { SnapshotSchema: "bbf", EventCapacity: 1, EventTypeCount: 1, EventPayloadWords: 8 },
    "Unexpected TargetRead snapshot or event contract.");

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
    Exercise(args.ElementAtOrDefault(1) ?? "matrix", null);
    return;
}
if (args.Length is not (4 or 5) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl") ||
    !int.TryParse(args[3], out int index) || index < 0)
    throw new ArgumentException("Usage: TargetReadConformance cpu [profile] | <vulkan|d3d12|d3d11|opengl> <bundle> <native library> <device index> [profile]");

var stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" => ".glsl", _ => ".dxil" })),
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
#if GA_BROWSER_CONFORMANCE
await Exercise(profile, startup);
#else
Exercise(profile, startup);
#endif

#if GA_BROWSER_CONFORMANCE
async Task Exercise(string profile, GaStartupResult? startup)
#else
void Exercise(string profile, GaStartupResult? startup)
#endif
{
    var (scenarios, ticks) = SelectCases(profile);
    var coverage = default(Coverage);
    foreach (var scenario in scenarios)
#if GA_BROWSER_CONFORMANCE
        coverage += await TargetScenario.Run(scenario with { Workers = 1 }, ticks, startup, contract);
#else
        coverage += TargetScenario.Run(scenario, ticks, startup, contract);
#endif
    Require(coverage.Rows > 0 && coverage.Self > 0 && coverage.Other > 0 && coverage.MissingTag > 0 &&
        coverage.Destroyed > 0 && coverage.Stale > 0 && coverage.Reused > 0 && coverage.Bodyless > 0,
        "The corpus missed a required target mode.");
    Require(startup is null ? coverage.DeviceRows == 0 && coverage.DeviceBatches == 0 :
        coverage.DeviceRows == coverage.Rows && coverage.DeviceBatches > 0, "Actual device work did not match expected routes.");
    Console.WriteLine($"TargetRead route={(startup is null ? "cpu" : "gpu")} profile={profile} cases={scenarios.Length} " +
        $"ticks={scenarios.Length * ticks} rows={coverage.Rows} self={coverage.Self} other={coverage.Other} " +
        $"missingTag={coverage.MissingTag} destroyed={coverage.Destroyed} stale={coverage.Stale} reused={coverage.Reused} " +
        $"bodyless={coverage.Bodyless} deviceRows={coverage.DeviceRows} deviceBatches={coverage.DeviceBatches} " +
        "exactState=passed exactTypedEvents=passed frozenPriorPosition=passed");
#if GA_BROWSER_CONFORMANCE
    Console.WriteLine("WebGPU TargetRead pendingReadback=passed");
#endif
}

static (Scenario[] Cases, int Ticks) SelectCases(string profile)
{
    int[] rows = [0, 1, 63, 64, 65, 127, 128, 129, 1024];
    uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
    int[] chunks = [1, 64, 127, 256];
    int[] budgets = [1, 3, 64];
    int[] workers = [1, 2, Environment.ProcessorCount];
    if (profile is "short" or "long")
        return ([new(65, 27, 127, 64, 2)], profile == "short" ? 1_000 : 100_000);
    if (profile is not ("smoke" or "matrix"))
        throw new ArgumentException($"Unknown TargetRead profile '{profile}'.");
    var scenarios = new List<Scenario>();
    foreach (int rowCount in rows)
    {
        int firstSeed = profile == "matrix" ? 0 : scenarios.Count % seeds.Length;
        int lastSeed = profile == "matrix" ? seeds.Length : firstSeed + 1;
        for (int seed = firstSeed; seed < lastSeed; seed++)
        {
            int i = scenarios.Count;
            scenarios.Add(new Scenario(rowCount, seeds[seed], chunks[i % chunks.Length],
                budgets[i % budgets.Length], workers[i % workers.Length]));
        }
    }
    return (scenarios.ToArray(), 8);
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
    if (backend == "opengl")
    {
        var gl = GaOpenGlBackend.Open(path);
        owner = gl;
        return gl.Devices;
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
