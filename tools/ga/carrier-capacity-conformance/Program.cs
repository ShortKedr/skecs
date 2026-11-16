using System.Diagnostics;
using System.Runtime.CompilerServices;
using SKECS;
using SKECS.GA;
#if !GA_BROWSER_CONFORMANCE
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif
using SKECS.Numerics;

#if !GA_BROWSER_CONFORMANCE
if (args is ["cpu", var cpuProfile])
{
    Qualify(cpuProfile, null, null, null);
    return;
}

if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Use 'cpu <profile>' or '<vulkan|d3d12|d3d11|opengl|gles> <bundle> <native bridge> <device index> <profile>'.");
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
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" or "gles" => ".glsl", _ => ".dxil" })),
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
#if GA_BROWSER_CONFORMANCE
await Qualify(profile, startup, contract, device);
#else
Qualify(profile, startup, contract, device);
#endif

#if GA_BROWSER_CONFORMANCE
static async Task Qualify(string profile, GaStartupResult? startup, GaContractIdentity? contract, GaDeviceInfo? device)
#else
static void Qualify(string profile, GaStartupResult? startup, GaContractIdentity? contract, GaDeviceInfo? device)
#endif
{
    int ticks;
    Scenario[] cases;
    switch (profile)
    {
        case "boundaries":
            ticks = 6;
            int[] sizes = [0, 1, 9, 63, 64, 65, 127, 128, 129, 1024];
            uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
            int[] chunks = [1, 64, 127, 256];
            int[] workers = [1, 2, Environment.ProcessorCount];
            int[] budgets = [1, 3, 64];
            cases = Enumerable.Range(0, sizes.Length * seeds.Length).Select(i => new Scenario(
                sizes[i / seeds.Length], seeds[i % seeds.Length], chunks[i % chunks.Length],
                chunks[(i + 1) % chunks.Length], workers[i % workers.Length],
                budgets[i % budgets.Length], budgets[(i + 1) % budgets.Length])).ToArray();
            break;
        case "short":
        case "long":
            ticks = profile == "short" ? 1_000 : 100_000;
            cases = [new(17, 7331, 127, 1, 2, 3, 64)];
            break;
        default:
            throw new ArgumentException($"Unknown profile '{profile}'.");
    }

    var timer = Stopwatch.StartNew();
    var counts = new Coverage();
    foreach (var scenario in cases)
#if GA_BROWSER_CONFORMANCE
        await RunCase(scenario with { Workers = 1 }, ticks, startup, contract, counts);
#else
        RunCase(scenario, ticks, startup, contract, counts);
#endif
    Require(counts.Outcomes.All(count => count > 0) && counts.SameTickReceipts > 0 &&
        counts.Retired > 0 && counts.ReusedAfterRetirement > 0 && counts.DomainTies > 0,
        $"The {profile} profile missed a required path.");
    string results = string.Join(" ", Enum.GetValues<BuildResult>().Select(result => $"{result}={counts.Outcomes[(int)result]}"));
    var lane = device == null ? "generated CPU" : $"{device.Api} {device.Device}";
    Console.WriteLine($"{lane}: profile={profile} cases={cases.Length} ticks={(long)cases.Length * ticks} " +
        $"requests={counts.Outcomes.Sum()} {results} domainTies={counts.DomainTies} observedBeforeRetire={counts.Visible} " +
        $"sameTickTickets={counts.SameTickReceipts} retired={counts.Retired} reusedAfterRetirement={counts.ReusedAfterRetirement} " +
        $"deviceBatches={counts.GpuBatches} deviceRows={counts.GpuRows} elapsedSeconds={timer.Elapsed.TotalSeconds:F2} allocator=passed");
#if GA_BROWSER_CONFORMANCE
    Console.WriteLine("WebGPU CarrierCapacity pendingReadback=passed");
#endif
}

#if GA_BROWSER_CONFORMANCE
static async Task RunCase(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity? contract, Coverage counts)
#else
static void RunCase(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity? contract, Coverage counts)
#endif
{
    BuildOrder[] orders = Enumerable.Range(0, scenario.Rows).Select(row => CarrierScenario.MakeOrder(scenario, row)).ToArray();
    var oracle = new CarrierOracle(orders);
    using var cpu = new World(new WorldOptions(scenario.CpuChunk));
    using var gpu = startup == null ? null : new World(new WorldOptions(scenario.GpuChunk));
    Initialize(cpu, orders);
    if (gpu != null)
        Initialize(gpu, orders);

    using var cpuRunner = new DeterministicRunner<CarrierFrame>(cpu, CreateWorkload(null), scenario.Workers);
    long caseGpuRows = 0;
    var processor = startup == null || contract == null ? null : new GaBatchProcessor<CarrierFrame>(
        startup, contract.SystemId, contract.SourceDigest, contract.IrDigest, contract.ComponentSchemaDigest,
        CarrierAdapterRowCodec.WordsPerRow, CarrierAdapterRowCodec.Pack<CarrierFrame>, CarrierAdapterRowCodec.Apply<CarrierFrame>,
        contract.EventCapacity, timing =>
        {
            caseGpuRows += timing.Rows;
            counts.GpuRows += timing.Rows;
            counts.GpuBatches++;
        }, null, false, [], [], CarrierAdapterRowCodec.TickResource, null, CarrierAdapterRowCodec.EventTypes);
    using var gpuRunner = gpu == null ? null : new DeterministicRunner<CarrierFrame>(gpu, CreateWorkload(processor));
    CheckState(cpu, oracle, $"CPU {scenario} initial");
    if (gpu != null)
        CheckState(gpu, oracle, $"GPU {scenario} initial");

    for (int tick = 1; tick <= ticks; tick++)
    {
        var frame = new CarrierFrame { Tick = tick };
        var expected = oracle.Advance(tick);
        cpuRunner.BeginTick(tick, frame, Fixed.One);
        gpuRunner?.BeginTick(tick, frame, Fixed.One);
#if GA_BROWSER_CONFORMANCE
        await RunToCompletion(cpuRunner, scenario.CpuBudget);
#else
        RunToCompletion(cpuRunner, scenario.CpuBudget);
#endif
        CheckEvents(cpuRunner.LastCompleted!, expected, tick, $"CPU {scenario}");
        CheckState(cpu, oracle, $"CPU {scenario} tick={tick}");
        if (gpuRunner != null && gpu != null)
        {
#if GA_BROWSER_CONFORMANCE
            bool waitedForReadback = await RunToCompletion(gpuRunner, scenario.GpuBudget);
            Require(scenario.Rows == 0 || waitedForReadback, $"WebGPU did not yield at {scenario}, tick {tick}.");
#else
            RunToCompletion(gpuRunner, scenario.GpuBudget);
#endif
            Require(caseGpuRows == (long)scenario.Rows * tick, $"Missing device rows at {scenario}, tick {tick}.");
            CheckEvents(gpuRunner.LastCompleted!, expected, tick, $"GPU {scenario}");
            CheckState(gpu, oracle, $"GPU {scenario} tick={tick}");
        }

        counts.Count(expected, tick);
        if (ticks >= 1_000 && tick % 1_000 == 0)
            Console.WriteLine($"progress tick={tick}/{ticks} deviceRows={caseGpuRows}");
    }
}

static DeterministicWorkload<CarrierFrame> CreateWorkload(GaBatchProcessor<CarrierFrame>? processor)
{
    var stateAccess = new DeterministicQuery().ReadWrite<TeamCapacity>().ReadWrite<HomeCapacity>();
    return new DeterministicWorkload<CarrierFrame>()
        .AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("carrier-team-order", 1), group =>
        {
            if (processor == null)
                CarrierAdapter.Register(group);
            else
                group.AddBatchSystem(CarrierAdapterRowCodec.CreateQuery(), processor);
            group.AddOrderedApply(stateAccess.Write<HangarShip>(), CarrierApply.Build);
        })
        .AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddSystem(new DeterministicQuery().With<HangarShip>().Read<HangarShip>(), new ObserveCommittedShips());
            group.AddOrderedApply(stateAccess, CarrierApply.Retire);
        });
}

static void Initialize(World world, BuildOrder[] orders)
{
    foreach (int limit in CarrierScenario.TeamLimits)
        world.CreateEntity(new TeamCapacity { Limit = limit });
    foreach (var home in CarrierScenario.Homes)
        world.CreateEntity(new HomeCapacity { Limit = home.Members, DockSlots = home.DockSlots, RepairSlots = home.RepairSlots });
    foreach (var row in orders)
        world.CreateEntity(row);
    world.DestroyEntity(world.CreateEntity());
}

#if GA_BROWSER_CONFORMANCE
static async Task<bool> RunToCompletion(DeterministicRunner<CarrierFrame> runner, int budget)
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
static void RunToCompletion(DeterministicRunner<CarrierFrame> runner, int budget)
{
    var status = DeterministicTickStatus.Pending;
    bool finished = SpinWait.SpinUntil(
        () => (status = runner.Advance(budget)) != DeterministicTickStatus.Pending, TimeSpan.FromSeconds(20));
    Require(finished && status == DeterministicTickStatus.Completed, $"Runner {status}: {runner.Fault}");
}
#endif

static void CheckEvents(DeterministicTickResult actual, ExpectedTick expected, int tick, string lane)
{
    string context = $"{lane} tick={tick}";
    var events = actual.Events;
    int index = 0;
    Require(actual.TickIndex == tick && events.Count == expected.Requests.Count + expected.Decisions.Count + expected.Visible.Count,
        context);
    foreach (var request in expected.Requests)
    {
        var item = events[index++];
        CheckKey(item.Key, tick, 0, DeterministicSourceKind.Entity, request.Source, 0, context);
        Require(item.Key.DomainKey is { } domain && domain.Primary == request.Payload.Team && domain.Secondary == request.Payload.Order,
            context);
        Require(item.Get<BuildRequest>().Equals(request.Payload), context);
    }
    foreach (var decision in expected.Decisions)
    {
        var item = events[index++];
        CheckKey(item.Key, tick, 1, DeterministicSourceKind.Job, default, decision.LocalOrdinal, context);
        Require(item.Key.DomainKey == null && item.Get<BuildDecision>().Equals(decision.Payload), context);
    }
    foreach (var visible in expected.Visible)
    {
        var item = events[index++];
        CheckKey(item.Key, tick, 2, DeterministicSourceKind.Entity, visible.Source, 0, context);
        Require(item.Key.DomainKey == null && item.Get<ShipVisible>().Equals(visible.Payload), context);
    }
}

static void CheckKey(DeterministicOrderKey key, int tick, int system, DeterministicSourceKind kind, CarrierAddress source,
    int ordinal, string label)
{
    Require(key.TickIndex == tick && key.Stage == SystemStage.Update && key.SystemOrder == system && key.ProducerOrder == 0 &&
        key.SourceKind == kind && key.SourceId == source.Id && key.SourceGeneration == source.Generation && key.LocalOrdinal == ordinal,
        label);
}

static void CheckState(World world, CarrierOracle oracle, string label)
{
    var checkpoint = world.OpenCheckpoint();
    Require(checkpoint.SlotCount == oracle.SlotCount, label);
    var free = new int[checkpoint.FreeCount];
    checkpoint.CopyFreeIds(free);
    Require(free.AsSpan().SequenceEqual(oracle.FreeIds.ToArray()), label);
    for (int id = 0; id < oracle.SlotCount; id++)
    {
        var actual = checkpoint.GetSlot(id);
        var expected = oracle.Slot(id);
        Require(actual.Alive == expected.Alive && actual.Version == expected.Generation, label);
    }

    int next = 0;
    for (int team = 0; team < CarrierScenario.TeamLimits.Length; team++)
    {
        var value = world.Get<TeamCapacity>(world.BindEntity(next++, 1));
        Require(value.Limit == CarrierScenario.TeamLimits[team] && value.Members == oracle.Ships.Count(item => item.Value.Team == team),
            label);
    }
    for (int home = 0; home < CarrierScenario.Homes.Length; home++)
    {
        var value = world.Get<HomeCapacity>(world.BindEntity(next++, 1));
        var limit = CarrierScenario.Homes[home];
        var docked = oracle.Ships.Where(item => item.Value.Home == home).Select(item => item.Value).ToArray();
        Require(value.Limit == limit.Members && value.DockSlots == limit.DockSlots && value.RepairSlots == limit.RepairSlots &&
            value.Members == docked.Length && value.OccupiedDocks == docked.Sum(ship => 1 << ship.Dock) &&
            value.Repairing == docked.Count(ship => ship.Repair), label);
    }
    foreach (var row in oracle.Orders)
        Require(world.Get<BuildOrder>(world.BindEntity(next++, 1)).Equals(row), label);
    foreach (var ship in oracle.Ships)
    {
        var entity = world.BindEntity(ship.Key, oracle.Slot(ship.Key).Generation);
        Require(checkpoint.GetComponentCount(entity) == 1 && world.Get<HangarShip>(entity).Equals(ship.Value), label);
    }
}

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

internal sealed class Coverage
{
    internal readonly long[] Outcomes = new long[Enum.GetValues<BuildResult>().Length];
    internal long DomainTies;
    internal long Visible;
    internal long SameTickReceipts;
    internal long Retired;
    internal long ReusedAfterRetirement;
    internal long GpuRows;
    internal long GpuBatches;

    internal void Count(ExpectedTick tick, int index)
    {
        foreach (var decision in tick.Decisions)
            Outcomes[(int)decision.Payload.Result]++;
        for (int i = 1; i < tick.Requests.Count; i++)
        {
            var previous = tick.Requests[i - 1].Payload;
            var current = tick.Requests[i].Payload;
            if (previous.Team == current.Team && previous.Order == current.Order)
                DomainTies++;
        }
        foreach (var item in tick.Visible)
        {
            Visible++;
            if (item.Payload.Retire)
                Retired++;
            if (item.Payload.Ship.BuiltAt != index)
                continue;
            SameTickReceipts++;
            if (index > 1 && item.Source.Generation > 1)
                ReusedAfterRetirement++;
        }
    }
}
