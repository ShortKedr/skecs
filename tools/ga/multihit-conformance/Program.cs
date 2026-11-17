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
using SKECS.Numerics;

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

static async ValueTask RunSuite(string lane, string profile, GaStartupResult? startup, GaContractIdentity? contract)
{
    var (cases, ticks) = SelectCases(profile);
#if GA_BROWSER_CONFORMANCE
    cases = Array.ConvertAll(cases, scenario => scenario with { CpuWorkers = 1 });
#endif
    var total = new Coverage();
    var finalPeriod = new Coverage();
    long revivals = 0;
    long deviceBatches = 0;
    long deviceRows = 0;
    foreach (var scenario in cases)
    {
        var expected = await RunCase(scenario, ticks, null, null, "CPU");
        total += expected.Counters;
        finalPeriod += expected.FinalPeriod;
        revivals += expected.Revivals;
        if (startup is not null && contract is not null)
        {
            var observed = await RunCase(scenario, ticks, startup, contract, "GPU");
            Check(observed.Counters == expected.Counters && observed.FinalPeriod == expected.FinalPeriod &&
                observed.Revivals == expected.Revivals, "Coverage differs between CPU and GPU.");
            deviceBatches += observed.DeviceBatches;
            deviceRows += observed.DeviceRows;
        }
    }
    Check(total.Hits == total.Applied + total.Killed + total.Dead + total.Stale &&
        total.Credited > 0 && total.Heat > 0 && total.Killed > 0 && total.Dead > 0 && total.Stale > 0 &&
        total.SameTickDeath > 0 && total.DeadAttackers > 0 && total.Ties > 0,
        "The scenarios missed a required ordered damage branch.");
    if (ticks >= 1_000)
        Check(revivals > 0 && finalPeriod.Heat > 0 && finalPeriod.Credited > 0 &&
            finalPeriod.SameTickDeath > 0 && finalPeriod.DeadAttackers > 0,
            "The final duration period missed a required layer, death or attribution branch.");
    Console.WriteLine($"{lane}: profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
        $"sizeBoundaries={string.Join(',', cases.Select(item => item.Rows).Distinct().Order())} " +
        $"seeds={string.Join(',', cases.Select(item => item.Seed).Distinct().Order())} " +
        $"workers={string.Join(',', cases.Select(item => item.CpuWorkers).Distinct().Order())} " +
        $"chunks={string.Join(',', cases.SelectMany(item => new[] { item.CpuChunk, item.GpuChunk }).Distinct().Order())} " +
        $"budgets={string.Join(',', cases.SelectMany(item => new[] { item.CpuBudget, item.GpuBudget }).Distinct().Order())} hits={total.Hits} " +
        $"applied={total.Applied} deaths={total.Killed} deadRejected={total.Dead} sameTickDeathRejected={total.SameTickDeath} " +
        $"staleRejected={total.Stale} credited={total.Credited} deadAttackerRejected={total.DeadAttackers} " +
        $"plasmaHeat={total.Heat} domainTies={total.Ties} events={total.Hits * 2 + total.Killed} " +
        $"revivals={revivals} finalPeriodHeat={finalPeriod.Heat} finalPeriodDeaths={finalPeriod.Killed} " +
        $"finalPeriodDeadAttackerRejected={finalPeriod.DeadAttackers} deviceBatches={deviceBatches} deviceRows={deviceRows} " +
        "requestAndDecisionKeys=passed exactShipState=passed aliveTag=passed singleDeath=passed");
}

static (Scenario[] Cases, int Ticks) SelectCases(string profile)
{
    if (profile is "short" or "long")
        return ([new(65, 27, 127, 64, 2, 3, 64)], profile == "short" ? 1_000 : 100_000);
    if (profile == "smoke")
        return ([
            new(0, 0, 1, 3, 1, 1, 1),
            new(1, 0, 64, 17, 2, 3, 1),
            new(63, 1, 127, 64, 2, 1, 3),
            new(64, 27, 17, 256, 1, 3, 1),
            new(65, 7331, 256, 7, 2, 64, 3),
            new(127, uint.MaxValue, 63, 127, 1, 1, 64),
            new(128, 27, 7, 63, 2, 3, 1),
            new(129, 0, 31, 67, 1, 3, 1),
            new(1024, uint.MaxValue, 128, 257, 2, 64, 3)
        ], 6);
    int[] sizes = [0, 1, 63, 64, 65, 127, 128, 129, 1024];
    uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
    int[] chunks = [1, 64, 127, 256];
    int[] workers = [1, 2, Environment.ProcessorCount];
    int[] budgets = [1, 3, 64];
    var cases = new List<Scenario>();
    foreach (int size in sizes)
    foreach (uint seed in seeds)
    {
        int i = cases.Count;
        cases.Add(new Scenario(size, seed, chunks[i % chunks.Length], chunks[(i + 1) % chunks.Length],
            workers[i % workers.Length], budgets[i % budgets.Length], budgets[(i + 1) % budgets.Length]));
    }
    return (cases.ToArray(), 6);
}

static async ValueTask<CaseResult> RunCase(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity? contract,
    string executor)
{
    bool onGpu = startup is not null;
    long deviceBatches = 0;
    long deviceRows = 0;
    void ObserveDeviceBatch(GaBatchTimings batch)
    {
        deviceBatches++;
        deviceRows += batch.Rows;
    }
    var plan = new DeterministicWorkload<DamageFrame>()
        .AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("hit-kind-impact-order", 1), group =>
        {
            if (startup is null)
                MultiHitAdapter.Register(group);
            else if (contract is not null)
                group.AddBatchSystem(MultiHitAdapterRowCodec.CreateQuery(), new GaBatchProcessor<DamageFrame>(
                    startup, contract.SystemId, contract.SourceDigest, contract.IrDigest, contract.ComponentSchemaDigest,
                    MultiHitAdapterRowCodec.WordsPerRow, MultiHitAdapterRowCodec.Pack<DamageFrame>,
                    MultiHitAdapterRowCodec.Apply<DamageFrame>, contract.EventCapacity, ObserveDeviceBatch, null, false, [], [],
                    MultiHitAdapterRowCodec.TickResource, null, MultiHitAdapterRowCodec.EventTypes));
            group.AddOrderedApply(new DeterministicQuery().ReadWrite<ShipDamage>().Write<AliveShip>(), DamageApplication.Apply);
        });
    using var world = new World(new WorldOptions(onGpu ? scenario.GpuChunk : scenario.CpuChunk));
    const int rowsPerTarget = 6;
    int targetCount = (scenario.Rows + rowsPerTarget - 1) / rowsPerTarget;
    var shipHandles = new Entity[targetCount + 2];
    var ships = new ShipDamage[shipHandles.Length];
    for (int i = 0; i < ships.Length; i++)
    {
        if (i == 2)
            world.DestroyEntity(world.CreateEntity());
        ships[i] = new ShipDamage
        {
            Team = i is 0 or 3 ? 1 : 0,
            Alive = true,
            Shield = Fixed.FromInt32(i == 2 ? 7 : 3 + (int)((scenario.Seed + (uint)i) % 7)),
            Hull = Fixed.FromInt32(i == 2 ? 5 : 38 + i),
            Armor = Fixed.FromRatio(1, 4),
            LastAttackerId = -1
        };
        var ship = world.CreateEntity(ships[i]);
        world.Add(ship, new AliveShip { Present = 1 });
        shipHandles[i] = ship;
        Check(ship.Id == i && (i != 2 || ship.Version > 1), "Ship allocation missed the reused target ID.");
    }

    var rows = new HitSource[scenario.Rows];
    var rowHandles = new Entity[rows.Length];
    for (int i = 0; i < rows.Length; i++)
    {
        int block = i / rowsPerTarget;
        var target = shipHandles[block + 2];
        int role = i % rowsPerTarget;
        var attacker = shipHandles[role == 1 && block == 1 ? 2 : role == 5 ? 1 : 0];
        HitKind kind = role switch { 0 => HitKind.Debris, 2 => HitKind.Contact, _ => HitKind.Projectile };
        DamageKind profile = role switch { 1 => DamageKind.Plasma, 3 => DamageKind.Kinetic, _ => DamageKind.Neutral };
        int damage = role switch { 0 => 24, 1 => 4, 2 => 4, 3 => 7, _ => 2 };
        rows[i] = new HitSource
        {
            FirstTick = role == 5 ? 2u : 1u,
            Request = new HitRequest
            {
                Kind = kind,
                ImpactOrder = block,
                TargetId = target.Id,
                TargetGeneration = role == 4 ? target.Version - 1 : target.Version,
                Impact = new Impact
                {
                    Profile = profile,
                    Amount = Fixed.FromRaw(((long)damage << 32) + (i >= rowsPerTarget ? (long)(i % 5) << 28 : 0)),
                    AttackerId = kind == HitKind.Projectile ? attacker.Id : -1,
                    AttackerGeneration = kind == HitKind.Projectile ? attacker.Version : 0
                }
            }
        };
        rowHandles[i] = world.CreateEntity(rows[i]);
        Check(rowHandles[i].Id == ships.Length + i, "Hit source IDs differ from corpus order.");
    }

    var oracle = new DamageOracle(ships, shipHandles, rows, rowHandles);
    using var runner = new DeterministicRunner<DamageFrame>(world, plan, workerCount: scenario.CpuWorkers);
    const int restorationPeriod = 8;
    int finalPeriodStart = (ticks - 1) / restorationPeriod * restorationPeriod + 1;
    var periodStartCounters = new Coverage();
    long revivals = 0;
    var description = $"{executor} {scenario}";
    for (uint tick = 1; tick <= ticks; tick++)
    {
        if (tick > 1 && (tick - 1) % restorationPeriod == 0)
        {
            // Reset the target fixture between completed ticks. Keep source rows, IDs and generations live.
            int restored = 0;
            for (int i = 2; i < ships.Length; i++)
            {
                if (!world.Has<AliveShip>(shipHandles[i]))
                {
                    world.Add(shipHandles[i], new AliveShip { Present = 1 });
                    restored++;
                }
                world.Set(shipHandles[i], ships[i]);
            }
            Check(restored == oracle.RestoreTargets(ships), $"{description} tick={tick}: revival count differs.");
            revivals += restored;
        }
        if (tick == finalPeriodStart)
            periodStartCounters = oracle.Coverage;
        var expected = oracle.Advance(tick);
        runner.BeginTick(tick, new DamageFrame { Tick = tick }, Fixed.One);
        int budget = onGpu ? scenario.GpuBudget : scenario.CpuBudget;
        var status = await Drain(runner, budget, onGpu);
        Check(status == DeterministicTickStatus.Completed,
            $"{description} tick={tick}: {status}, fault {runner.Fault}.");
        Check(deviceRows == (onGpu ? (long)scenario.Rows * tick : 0),
            $"{description} tick={tick}: unexpected number of completed device rows ({deviceRows}).");
        var completed = runner.LastCompleted!;
        try
        {
            Check(completed.TickIndex == tick && completed.Events.Count == expected.Count, "Wrong completed event count.");
            for (int i = 0; i < expected.Count; i++)
            {
                var actual = completed.Events[i];
                var reference = expected[i];
                var key = actual.Key;
                bool isRequest = reference.Payload is HitRequest;
                if (key.TickIndex != tick || key.Stage != SystemStage.Update || key.SystemOrder != (isRequest ? 0 : 1) ||
                    key.ProducerOrder != 0 || key.SourceKind != (isRequest ? DeterministicSourceKind.Entity : DeterministicSourceKind.Job) ||
                    key.SourceId != reference.SourceId || key.SourceGeneration != reference.SourceGeneration ||
                    key.LocalOrdinal != reference.Ordinal || !Nullable.Equals(key.DomainKey, reference.Domain))
                    throw new InvalidOperationException($"Event {i} has wrong order key {key}.");
                if (actual.PayloadType != reference.Payload.GetType())
                    throw new InvalidOperationException($"Event {i} has payload type {actual.PayloadType.Name}.");
                bool samePayload = reference.Payload switch
                {
                    HitRequest hit => actual.Get<HitRequest>().Equals(hit),
                    HitDecision decision => actual.Get<HitDecision>().Equals(decision),
                    ShipDeath death => actual.Get<ShipDeath>().Equals(death),
                    _ => false
                };
                if (!samePayload)
                    throw new InvalidOperationException($"Event {i} ({reference.Payload.GetType().Name}) has a different payload.");
            }
            for (int i = 0; i < ships.Length; i++)
            {
                var expectedShip = oracle.ShipAt(i);
                if (!world.Get<ShipDamage>(shipHandles[i]).Equals(expectedShip) ||
                    world.Has<AliveShip>(shipHandles[i]) != expectedShip.Alive)
                    throw new InvalidOperationException($"Ship {i} differs.");
            }
            for (int i = 0; i < rows.Length; i++)
            {
                var expectedRow = rows[i];
                expectedRow.Emissions = tick >= expectedRow.FirstTick ? tick - expectedRow.FirstTick + 1 : 0;
                if (!world.Get<HitSource>(rowHandles[i]).Equals(expectedRow))
                    throw new InvalidOperationException($"Hit source {i} differs.");
            }
            Check(world.GetStorageStatistics().EntityCount == shipHandles.Length + rowHandles.Length,
                "Death marker removal unexpectedly changed entity count.");
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                Lane = description,
                Tick = tick,
                Error = error.Message,
                SeenShips = shipHandles.Select(entity => new { entity.Id, entity.Version,
                    State = world.TryGet<ShipDamage>(entity, out var state) ? (ShipDamage?)state : null,
                    AliveTag = world.IsAlive(entity) && world.Has<AliveShip>(entity) }),
                ExpectedShips = Enumerable.Range(0, ships.Length).Select(oracle.ShipAt),
                SeenSources = rowHandles.Select(entity => new { entity.Id, entity.Version,
                    State = world.TryGet<HitSource>(entity, out var state) ? (HitSource?)state : null }),
                ExpectedSources = rows.Select(row =>
                {
                    row.Emissions = tick >= row.FirstTick ? tick - row.FirstTick + 1 : 0;
                    return row;
                }),
                ExpectedEvents = expected,
                SeenEvents = completed.Events.Select(item => new { item.Key, Payload = EventPayload(item) })
            }, new JsonSerializerOptions { IncludeFields = true }));
            throw;
        }
        if (ticks >= 1_000 && tick % 1_000 == 0)
            Console.WriteLine($"progress lane={executor} rows={scenario.Rows} seed={scenario.Seed} tick={tick}/{ticks} " +
                $"deviceBatches={deviceBatches} deviceRows={deviceRows} revivals={revivals}");
    }
    return new CaseResult(oracle.Coverage, oracle.Coverage - periodStartCounters, revivals, deviceBatches, deviceRows);
}

static object EventPayload(DeterministicEvent item)
{
    var type = item.PayloadType;
    if (type == typeof(HitRequest))
        return item.Get<HitRequest>();
    if (type == typeof(HitDecision))
        return item.Get<HitDecision>();
    if (type == typeof(ShipDeath))
        return item.Get<ShipDeath>();
    return type.FullName ?? type.Name;
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

internal readonly record struct Scenario(int Rows, uint Seed, int CpuChunk, int GpuChunk, int CpuWorkers, int CpuBudget, int GpuBudget);
internal readonly record struct CaseResult(Coverage Counters, Coverage FinalPeriod, long Revivals, long DeviceBatches, long DeviceRows);
