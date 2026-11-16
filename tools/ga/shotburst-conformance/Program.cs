using System.Diagnostics;
using System.Text.Json;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;

if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0 ||
    args[4] is not ("smoke" or "matrix" or "short" or "long"))
    throw new ArgumentException("Usage: ShotBurstConformance <vulkan|d3d12> <bundle> <native bridge> <device index> <smoke|matrix|short|long>");

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

var stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] == "vulkan" ? ".spv" : ".dxil")),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
var devices = Open(args[0], args[2], out var owner);
using (owner)
{
    Check(deviceIndex < devices.Count, $"Device {deviceIndex} is outside the {devices.Count} enumerated devices.");
    using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])],
        [devices[deviceIndex]], allowExperimental: true);
    Check(startup.Succeeded && startup.Decisions.Single().Route == GaRoute.Gpu,
        string.Join(", ", startup.Decisions.Select(decision => $"{decision.Reason} {decision.Detail}")));
    var manifest = artifact.Bundle!.Manifest;
    var device = devices[deviceIndex].Info;
    var profile = args[4];
    var (cases, ticks) = SelectCases(profile);
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
        var (counters, batches, rows) = RunCase(startup, contract, scenario, ticks);
        total = total.With(counters);
        deviceBatches += batches;
        deviceRows += rows;
    }
    Check(total.Requests == total.Accepted + total.Rejected && total.Accepted > 0 && total.Rejected > 0 &&
        total.HeatRejected > 0 && total.ZeroReseeds > 0, "The matrix missed a required gameplay branch.");
    Console.WriteLine($"{args[0]} {device.Device}: profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
        $"sizeBoundaries={string.Join(',', cases.Select(item => item.Rows).Distinct().Order())} " +
        $"seeds={string.Join(',', cases.Select(item => item.Seed).Distinct().Order())} " +
        $"requests={total.Requests} decisions={total.Accepted + total.Rejected} events={2 * total.Requests} " +
        $"accepted={total.Accepted} capacityRejected={total.Rejected} heatRejected={total.HeatRejected} zeroReseeds={total.ZeroReseeds} " +
        $"deviceBatches={deviceBatches} deviceRows={deviceRows} elapsedSeconds={timer.Elapsed.TotalSeconds:F2} " +
        "domainAndOrdinals=passed fixedAndRng=passed ticketIds=passed reusedSourceGeneration=passed");
}

static (Scenario[] Cases, int Ticks) SelectCases(string profile)
{
    if (profile is "short" or "long")
        return ([new(65, 0, 127, 64, 3, 64, 2)], profile == "short" ? 1_000 : 100_000);

    if (profile == "smoke")
        return (
        [
            new(0, 0, 1, 3, 1, 1, 2),
            new(1, 0, 64, 17, 3, 1, 2),
            new(63, 1, 127, 64, 1, 3, 2),
            new(64, 27, 17, 256, 3, 1, 2),
            new(65, 7331, 256, 7, 64, 3, 2),
            new(127, uint.MaxValue, 63, 127, 1, 64, 2),
            new(128, 27, 7, 63, 3, 1, 2),
            new(129, 0, 31, 67, 3, 1, 2),
            new(1024, uint.MaxValue, 128, 257, 64, 3, 2)
        ], 6);

    int[] sizes = [0, 1, 63, 64, 65, 127, 128, 129, 1024];
    uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
    int[] chunks = [1, 64, 127, 256];
    int[] budgets = [1, 3, 64];
    int[] workers = [1, 2, Environment.ProcessorCount];
    var cases = new List<Scenario>();
    foreach (int size in sizes)
    foreach (uint seed in seeds)
    {
        int i = cases.Count;
        cases.Add(new Scenario(size, seed, chunks[i % chunks.Length], chunks[(i + 1) % chunks.Length],
            budgets[i % budgets.Length], budgets[(i + 1) % budgets.Length], workers[i % workers.Length]));
    }
    return (cases.ToArray(), 6);
}

static (Totals Counters, long DeviceBatches, long DeviceRows) RunCase(GaStartupResult startup, GaContractIdentity contract,
    Scenario scenario, int ticks)
{
    long deviceBatches = 0;
    long deviceRows = 0;
    void ObserveDeviceBatch(GaBatchTimings batch)
    {
        deviceBatches++;
        deviceRows += batch.Rows;
    }

    var order = new DeterministicDomainOrder("shot-serial-slot", 1);
    var cpuPlan = new DeterministicWorkload<ShotFrame>().AddCommitGroup(SystemStage.Update, order, group =>
    {
        ShotBurstAdapter.Register(group);
        ShotBurstAdapter.Register(group);
        group.AddOrderedApply(new DeterministicQuery().ReadWrite<CapacityState>().Write<Projectile>(), ShotApplication.Apply);
    });
    var gpuPlan = new DeterministicWorkload<ShotFrame>().AddCommitGroup(SystemStage.Update, order, group =>
    {
        for (int producer = 0; producer < 2; producer++)
            group.AddBatchSystem(ShotBurstAdapterRowCodec.CreateQuery(),
                new GaBatchProcessor<ShotFrame>(startup, contract.SystemId, contract.SourceDigest,
                    contract.IrDigest, contract.ComponentSchemaDigest, ShotBurstAdapterRowCodec.WordsPerRow,
                    ShotBurstAdapterRowCodec.Pack<ShotFrame>, ShotBurstAdapterRowCodec.Apply<ShotFrame>, contract.EventCapacity,
                    ObserveDeviceBatch, null, false, [], [], ShotBurstAdapterRowCodec.TickResource, null, ShotBurstAdapterRowCodec.EventTypes));
        group.AddOrderedApply(new DeterministicQuery().ReadWrite<CapacityState>().Write<Projectile>(), ShotApplication.Apply);
    });
    using var cpuWorld = new World(new WorldOptions(scenario.CpuChunk));
    using var gpuWorld = new World(new WorldOptions(scenario.GpuChunk));
    var cpuCapacity = cpuWorld.CreateEntity(new CapacityState());
    var gpuCapacity = gpuWorld.CreateEntity(new CapacityState());
    Check(cpuCapacity.Id == gpuCapacity.Id && cpuCapacity.Version == gpuCapacity.Version && cpuCapacity.Id == 0,
        "Capacity identity differs between routes.");

    if (scenario.Rows > 0)
    {
        var discardedCpu = cpuWorld.CreateEntity();
        var discardedGpu = gpuWorld.CreateEntity();
        cpuWorld.DestroyEntity(discardedCpu);
        gpuWorld.DestroyEntity(discardedGpu);
    }
    var initial = new MountRow[scenario.Rows];
    var cpuHandles = new Entity[scenario.Rows];
    var gpuHandles = new Entity[scenario.Rows];
    for (int row = 0; row < scenario.Rows; row++)
    {
        // Reversed serials force domain order to differ from Entity.Id.
        initial[row] = new MountRow { Serial = scenario.Rows - 1 - row, Heat = (row * 5 + (int)(scenario.Seed % 5)) % 9 };
        cpuHandles[row] = cpuWorld.CreateEntity(initial[row]);
        gpuHandles[row] = gpuWorld.CreateEntity(initial[row]);
        Check(cpuHandles[row].Id == row + 1 && cpuHandles[row].Id == gpuHandles[row].Id &&
            cpuHandles[row].Version == gpuHandles[row].Version, $"Mount allocation differs at row {row}.");
    }
    if (scenario.Rows > 0)
        Check(cpuHandles[0].Version > 1, "The first mount did not reuse an entity generation.");

    using var cpu = new DeterministicRunner<ShotFrame>(cpuWorld, cpuPlan, workerCount: scenario.Workers);
    using var gpu = new DeterministicRunner<ShotFrame>(gpuWorld, gpuPlan);
    var oracle = new BurstOracle(initial, cpuHandles.Select(entity => new Handle(entity.Id, entity.Version)).ToArray());
    var cpuLane = $"CPU {scenario}";
    var gpuLane = $"GPU {scenario}";
    for (uint tick = 1; tick <= ticks; tick++)
    {
        var frame = new ShotFrame
        {
            Seed = scenario.Seed,
            Tick = tick,
            MaxProjectiles = tick switch { 1 => 0, 2 => 1 + scenario.Rows % 7, 3 => 1, 4 => 3 + scenario.Rows % 23,
                5 => 5 + scenario.Rows % 37, _ => 6 + scenario.Rows % 37 },
            Cooling = 1 + (int)(tick % 4),
            HeatCost = 1 + (int)(tick % 3),
            HeatLimit = 5 + (int)(tick % 3),
            Burst = (int)(tick % 3),
            CapacityId = cpuCapacity.Id,
            CapacityGeneration = cpuCapacity.Version
        };
        var events = oracle.Advance(frame);
        cpu.BeginTick(tick, frame, Fixed.One);
        gpu.BeginTick(tick, frame, Fixed.One);
        Drain(cpu, scenario.CpuBudget);
        Drain(gpu, scenario.GpuBudget);
        Check(deviceRows == 2L * scenario.Rows * tick, $"{gpuLane}: missing device work at tick {tick}.");
        VerifyCompletedLane(cpuWorld, cpu, cpuCapacity, cpuHandles, oracle, events, tick, cpuLane);
        VerifyCompletedLane(gpuWorld, gpu, gpuCapacity, gpuHandles, oracle, events, tick, gpuLane);
        if (ticks >= 1_000 && tick % 1_000 == 0)
            Console.WriteLine($"progress tick={tick}/{ticks} deviceBatches={deviceBatches} deviceRows={deviceRows}");
    }
    return (oracle.Counters, deviceBatches, deviceRows);
}

static void VerifyCompletedLane(World world, DeterministicRunner<ShotFrame> runner, Entity capacity, Entity[] handles,
    BurstOracle oracle, TickEvents expected, uint tick, string lane)
{
    try
    {
        CheckLane(world, runner, capacity, handles, oracle, expected, tick, lane);
    }
    catch
    {
        var observed = runner.LastCompleted!.Events.Select(item => new
        {
            item.Key,
            Type = item.PayloadType.FullName,
            Payload = item.PayloadType == typeof(ShotRequest) ? (object)item.Get<ShotRequest>() :
                item.PayloadType == typeof(ShotDecision) ? item.Get<ShotDecision>() : null
        }).ToArray();
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            Lane = lane,
            Tick = tick,
            Expected = new { Events = expected, oracle.Mounts, oracle.Projectiles },
            Observed = new { Events = observed, Mounts = handles.Select(entity => world.Get<MountRow>(entity)).ToArray(),
                Projectiles = CaptureProjectiles(world), Capacity = world.Get<CapacityState>(capacity) }
        }, new JsonSerializerOptions { IncludeFields = true }));
        throw;
    }
}

static void CheckLane(World world, DeterministicRunner<ShotFrame> runner, Entity capacity, Entity[] handles,
    BurstOracle oracle, TickEvents expected, uint tick, string lane)
{
    var completed = runner.LastCompleted!;
    var actual = completed.Events;
    Check(completed.TickIndex == tick && actual.Count == expected.Requests.Length + expected.Decisions.Length,
        $"{lane}: wrong completed tick or event count at {tick}.");
    for (int i = 0; i < expected.Requests.Length; i++)
    {
        var oracleEvent = expected.Requests[i];
        var request = actual[i].Get<ShotRequest>();
        Check(request.Equals(oracleEvent.Payload),
            $"{lane}: wrong request at tick {tick}, event {i}, Serial={oracleEvent.Payload.Serial}, slot={oracleEvent.Payload.Slot}.");
        var key = actual[i].Key;
        Check(key.TickIndex == tick && key.Stage == SystemStage.Update && key.SystemOrder == 0 &&
            key.ProducerOrder == oracleEvent.Producer && key.SourceKind == DeterministicSourceKind.Entity &&
            key.SourceId == oracleEvent.Source.Id && key.SourceGeneration == oracleEvent.Source.Generation &&
            key.LocalOrdinal == oracleEvent.Ordinal && key.DomainKey is { } domain &&
            domain.Primary == oracleEvent.Payload.Serial && domain.Secondary == oracleEvent.Payload.Slot,
            $"{lane}: wrong request key at tick {tick}, event {i}.");
    }
    for (int i = 0; i < expected.Decisions.Length; i++)
    {
        var item = actual[expected.Requests.Length + i];
        var oracleEvent = expected.Decisions[i];
        Check(item.Get<ShotDecision>().Equals(oracleEvent.Payload), $"{lane}: wrong decision at tick {tick}, index {i}.");
        var key = item.Key;
        Check(key.TickIndex == tick && key.Stage == SystemStage.Update && key.SystemOrder == 1 &&
            key.ProducerOrder == 0 && key.SourceKind == DeterministicSourceKind.Job && key.SourceId == 0 &&
            key.SourceGeneration == 0 && key.LocalOrdinal == oracleEvent.Ordinal && key.DomainKey == null,
            $"{lane}: wrong decision key at tick {tick}, index {i}.");
    }
    for (int row = 0; row < handles.Length; row++)
        Check(world.Get<MountRow>(handles[row]).Equals(oracle.Mounts[row]), $"{lane}: mount {row} differs after tick {tick}.");
    Check(world.Get<CapacityState>(capacity).Active == oracle.Projectiles.Count,
        $"{lane}: wrong active projectile count after tick {tick}.");
    var spawned = CaptureProjectiles(world);
    Check(spawned.SequenceEqual(oracle.Projectiles),
        $"{lane}: ticket-resolved projectile IDs, generations or payloads differ after tick {tick}.");
    Check(world.GetStorageStatistics().EntityCount == handles.Length + 1 + spawned.Count,
        $"{lane}: capacity rejection unexpectedly changed world membership at tick {tick}.");
}

static unsafe List<Spawn> CaptureProjectiles(World world)
{
    var spawned = new List<Spawn>();
    foreach (var chunk in world.Query<Projectile>().Chunks())
        for (int i = 0; i < chunk.Count; i++)
            spawned.Add(new Spawn(chunk.Entities[i], chunk.Versions[i], chunk.Component1[i]));
    spawned.Sort((left, right) => left.Id.CompareTo(right.Id));
    return spawned;
}

static void Drain(DeterministicRunner<ShotFrame> runner, int budget)
{
    long started = Stopwatch.GetTimestamp();
    var spin = new SpinWait();
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed.TotalSeconds >= 20)
            throw new TimeoutException("The runner stayed pending for 20 seconds.");
        if (elapsed.TotalMilliseconds < 2)
            spin.SpinOnce(sleep1Threshold: -1);
        else
            Thread.Sleep(1);
        status = runner.Advance(budget);
    }
    if (status != DeterministicTickStatus.Completed)
        throw new InvalidOperationException($"Expected Completed, got {status}.", runner.Fault);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static IReadOnlyList<IGaDevice> Open(string backend, string library, out IDisposable owner)
{
    if (backend == "vulkan")
    {
        var vk = GaVulkanBackend.Open(library);
        owner = vk;
        return vk.Devices;
    }
    var dx = GaD3D12Backend.Open(library);
    owner = dx;
    return dx.Devices;
}

internal readonly record struct Scenario(int Rows, uint Seed, int CpuChunk, int GpuChunk, int CpuBudget, int GpuBudget, int Workers);
internal readonly record struct Handle(int Id, int Generation);
internal readonly record struct Spawn(int Id, int Generation, Projectile Payload);
internal readonly record struct OracleRequest(ShotRequest Payload, Handle Source, int Producer, int Ordinal);
internal readonly record struct OracleDecision(ShotDecision Payload, int Ordinal);
internal readonly record struct TickEvents(OracleRequest[] Requests, OracleDecision[] Decisions);
internal readonly record struct Totals(long Requests, long Accepted, long Rejected, long HeatRejected, long ZeroReseeds)
{
    public Totals With(Totals other) => new(Requests + other.Requests, Accepted + other.Accepted,
        Rejected + other.Rejected, HeatRejected + other.HeatRejected, ZeroReseeds + other.ZeroReseeds);
}

internal sealed class BurstOracle(MountRow[] mounts, Handle[] sources)
{
    public MountRow[] Mounts { get; } = mounts;
    public List<Spawn> Projectiles { get; } = new();
    public Totals Counters { get; private set; }

    public TickEvents Advance(ShotFrame frame)
    {
        var requests = new List<OracleRequest>();
        var heatRejected = 0;
        var zeroReseeds = 0;
        for (int rowIndex = 0; rowIndex < Mounts.Length; rowIndex++)
        {
            ref var row = ref Mounts[rowIndex];
            row.LastTick = frame.Tick;
            row.Heat = Math.Max(0, row.Heat - frame.Cooling);
            if (!row.Initialized)
            {
                uint seed = frame.Seed ^ (uint)((ulong)row.Serial * 0x85ebca6b & uint.MaxValue);
                if (seed == 0)
                    zeroReseeds++;
                row.Rng = seed != 0 ? seed : 0x6d2b79f5;
                row.Initialized = true;
            }
            row.Slots = 4;

            // Schedule by logical slot and compute the producer and local event ordinal explicitly.
            var ordinals = new int[2];
            for (int slot = 0; slot < row.Slots; slot++)
            {
                if (slot % 2 >= frame.Burst)
                    continue;
                int availableHeat = frame.HeatLimit - row.Heat;
                if (availableHeat < frame.HeatCost)
                {
                    heatRejected++;
                    continue;
                }

                row.Heat += frame.HeatCost;
                row.Rng = NextDraw(row.Rng);
                var producer = slot / 2;
                var request = new ShotRequest
                {
                    Serial = row.Serial, Slot = slot, Heat = row.Heat, Draw = row.Rng,
                    Spread = new ShotSpread
                    {
                        Offset = Fixed.FromRaw(((long)(row.Rng % 8) - 3) * 4_294_967_296L - 1_073_741_824L),
                        Turret = slot >= 2
                    }
                };
                requests.Add(new OracleRequest(request, sources[rowIndex], producer, ordinals[producer]++));
            }
        }

        var ordered = requests.OrderBy(item => item.Payload.Serial).ThenBy(item => item.Payload.Slot).ToArray();
        int before = Projectiles.Count;
        int slotsRemaining = Math.Max(0, frame.MaxProjectiles - before);
        var decisions = new OracleDecision[ordered.Length];
        var ordinal = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            bool accepted = index < slotsRemaining;
            var request = ordered[index].Payload;
            if (accepted)
            {
                var data = new Projectile
                {
                    Serial = request.Serial, Slot = request.Slot, Draw = request.Draw, Tick = frame.Tick, Offset = request.Spread.Offset
                };
                Projectiles.Add(new Spawn(1 + Mounts.Length + Projectiles.Count, 1, data));
                ordinal += 2; // The accepted decision follows the Create and payload Add on its job source.
            }
            var decision = new ShotDecision { Serial = request.Serial, Slot = request.Slot, Draw = request.Draw, Accepted = accepted };
            decisions[index] = new OracleDecision(decision, ordinal++);
        }
        int acceptedThisTick = Projectiles.Count - before;
        Counters = Counters.With(new Totals(ordered.Length, acceptedThisTick, ordered.Length - acceptedThisTick, heatRejected, zeroReseeds));
        return new TickEvents(ordered, decisions);
    }

    private static uint NextDraw(uint value)
    {
        const ulong modulo = 1UL << 32;
        ulong left = (value ^ ((ulong)value * 8192UL)) % modulo;
        ulong middle = left ^ (left / 131072UL);
        return (uint)((middle ^ (middle * 32UL)) % modulo);
    }
}
