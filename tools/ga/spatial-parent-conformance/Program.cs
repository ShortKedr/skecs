using System.Text.Json;
using SKECS.GA;
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;

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

if (args.Length is 1 or 2 && args[0] == "cpu")
{
    Exercise(args.ElementAtOrDefault(1) ?? "matrix", null);
    return;
}
if (args.Length is not (4 or 5) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl") ||
    !int.TryParse(args[3], out int index) || index < 0)
    throw new ArgumentException("Usage: SpatialParentConformance cpu [profile] | <vulkan|d3d12|d3d11|opengl> <bundle> <native library> <device index> [profile]");

var stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" => ".glsl", _ => ".dxil" })),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
var devices = Open(args[0], args[2], out var owner);
using (owner)
{
    Require(index < devices.Count, $"Device index {index} exceeds available devices {devices.Count}.");
    var device = devices[index];
    using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [device], allowExperimental: true);
    var route = startup.Decisions.Single();
    Require(startup.Succeeded && route.Route == GaRoute.Gpu, $"GPU startup failed: {route.Reason} {route.Detail}");
    var manifest = artifact.Bundle!.Manifest;
    Console.WriteLine($"device={device.Info.Device} api={device.Info.Api} version={device.Info.ApiVersion} driver={device.Info.Driver} " +
        $"source={contract.SourceDigest} ir={contract.IrDigest} irVersion={contract.IrVersion} subsetVersion={contract.SubsetVersion} " +
        $"numericVersion={contract.NumericVersion} artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} " +
        $"contentId={route.ArtifactId} workgroup={manifest.Requirements.WorkgroupX}");
    Console.WriteLine($"toolchain={JsonSerializer.Serialize(manifest.Toolchain)}");
    Exercise(args.ElementAtOrDefault(4) ?? "matrix", startup);
}

void Exercise(string profile, GaStartupResult? startup)
{
    var scenarios = SelectCases(profile);
    var totals = default(Totals);
    foreach (var scenario in scenarios)
        totals += SpatialScenario.Run(scenario, startup, contract);
    Require(totals.Witnesses == Witness.All, $"The corpus missed {Witness.All & ~totals.Witnesses}.");
    Require(totals.Queries > 0 && (startup is null ? totals.Batches == 0 : totals.Batches > 0),
        "The corpus did not execute its selected route.");
    Console.WriteLine($"SpatialParent route={(startup is null ? "cpu" : "gpu")} profile={profile} cases={scenarios.Length} " +
        $"ticks={8 * scenarios.Length} queries={totals.Queries} candidateOccurrences={totals.Candidates} duplicates={totals.Duplicates} " +
        $"exactUnique={totals.Exact} staleTargets={totals.Stale} deviceRows={(startup is null ? 0 : totals.Queries)} " +
        $"deviceBatches={totals.Batches} witnesses={totals.Witnesses} " +
        "exactState=passed exactTypedEvents=passed bruteForceCoverage=passed sealAddClear=passed");
}

static Scenario[] SelectCases(string profile)
{
    int[] rows = [0, 1, 63, 64, 65, 127, 128, 129, 1024];
    uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
    int[] chunks = [1, 64, 127, 256];
    int[] budgets = [1, 3, 64];
    if (profile is not ("smoke" or "matrix"))
        throw new ArgumentException($"Unknown SpatialParent profile '{profile}'.");
    var scenarios = new List<Scenario>();
    foreach (int rowCount in rows)
    {
        int firstSeed = profile == "matrix" ? 0 : scenarios.Count % seeds.Length;
        int lastSeed = profile == "matrix" ? seeds.Length : firstSeed + 1;
        for (int seed = firstSeed; seed < lastSeed; seed++)
        {
            int i = scenarios.Count;
            scenarios.Add(new Scenario(rowCount, seeds[seed], chunks[i % chunks.Length], budgets[i % budgets.Length]));
        }
    }
    return scenarios.ToArray();
}

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

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
}
