#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

namespace ShotBurstFixture
{
    internal static class ShotBurstScenario
    {
        internal static (Scenario[] Cases, int Ticks) SelectCases(string profile, bool singleWorker)
        {
            int smokeWorkers = singleWorker ? 1 : 2;
            if (profile is "short" or "long")
                return (new Scenario[] { new(65, 0, 127, 64, 3, 64, smokeWorkers) }, profile == "short" ? 1_000 : 100_000);

            if (profile == "smoke")
                return (new Scenario[]
                {
                    new(0, 0, 1, 3, 1, 1, smokeWorkers),
                    new(1, 0, 64, 17, 3, 1, smokeWorkers),
                    new(63, 1, 127, 64, 1, 3, smokeWorkers),
                    new(64, 27, 17, 256, 3, 1, smokeWorkers),
                    new(65, 7331, 256, 7, 64, 3, smokeWorkers),
                    new(127, uint.MaxValue, 63, 127, 1, 64, smokeWorkers),
                    new(128, 27, 7, 63, 3, 1, smokeWorkers),
                    new(129, 0, 31, 67, 3, 1, smokeWorkers),
                    new(1024, uint.MaxValue, 128, 257, 64, 3, smokeWorkers)
                }, 6);

            int[] sizes = { 0, 1, 63, 64, 65, 127, 128, 129, 1024 };
            uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
            int[] chunks = { 1, 64, 127, 256 };
            int[] budgets = { 1, 3, 64 };
            int[] workers = singleWorker ? new[] { 1 } : new[] { 1, 2, Environment.ProcessorCount };
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

        internal static void VerifyCoverage(Totals total)
        {
            Check(total.Requests == total.Accepted + total.Rejected && total.Accepted > 0 && total.Rejected > 0 &&
                total.HeatRejected > 0 && total.ZeroReseeds > 0, "The matrix missed a required gameplay branch.");
        }

        internal static async ValueTask<(Totals Counters, long DeviceBatches, long DeviceRows)> RunCase(
            Scenario scenario, int ticks, bool useGpu,
            Action<DeterministicCommitGroup<ShotFrame>> registerCpu,
            Action<DeterministicCommitGroup<ShotFrame>, Action<GaBatchTimings>> registerTarget,
            Func<DeterministicRunner<ShotFrame>, int, bool, ValueTask> drain, Action<string> report, Action<object>? reportMismatch = null)
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
                registerCpu(group);
                registerCpu(group);
                group.AddOrderedApply(new DeterministicQuery().ReadWrite<CapacityState>().Write<Projectile>(), ShotApplication.Apply);
            });
            var gpuPlan = new DeterministicWorkload<ShotFrame>().AddCommitGroup(SystemStage.Update, order, group =>
            {
                for (int producer = 0; producer < 2; producer++)
                    registerTarget(group, ObserveDeviceBatch);
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
            var gpuLane = $"{(useGpu ? "GPU" : "CPU fallback")} {scenario}";
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
                await drain(cpu, scenario.CpuBudget, false);
                await drain(gpu, scenario.GpuBudget, useGpu);
                Check(deviceRows == (useGpu ? 2L * scenario.Rows * tick : 0), $"{gpuLane}: unexpected device work at tick {tick}.");
                VerifyCompletedLane(cpuWorld, cpu, cpuCapacity, cpuHandles, oracle, events, tick, cpuLane, reportMismatch);
                VerifyCompletedLane(gpuWorld, gpu, gpuCapacity, gpuHandles, oracle, events, tick, gpuLane, reportMismatch);
                if (ticks >= 1_000 && tick % 1_000 == 0)
                    report($"progress tick={tick}/{ticks} deviceBatches={deviceBatches} deviceRows={deviceRows}");
            }
            return (oracle.Counters, deviceBatches, deviceRows);
        }

        static void VerifyCompletedLane(World world, DeterministicRunner<ShotFrame> runner, Entity capacity, Entity[] handles,
            BurstOracle oracle, TickEvents expected, uint tick, string lane, Action<object>? reportMismatch)
        {
            try
            {
                CheckLane(world, runner, capacity, handles, oracle, expected, tick, lane);
            }
            catch when (reportMismatch is not null)
            {
                var observed = runner.LastCompleted!.Events.Select(item => new
                {
                    item.Key,
                    Type = item.PayloadType.FullName,
                    Payload = item.PayloadType == typeof(ShotRequest) ? (object)item.Get<ShotRequest>() :
                        item.PayloadType == typeof(ShotDecision) ? item.Get<ShotDecision>() : null
                }).ToArray();
                reportMismatch(new
                {
                    Lane = lane,
                    Tick = tick,
                    Expected = new { Events = expected, oracle.Mounts, oracle.Projectiles },
                    Observed = new { Events = observed, Mounts = handles.Select(entity => world.Get<MountRow>(entity)).ToArray(),
                        Projectiles = CaptureProjectiles(world), Capacity = world.Get<CapacityState>(capacity) }
                });
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

        static List<Spawn> CaptureProjectiles(World world)
        {
            var spawned = new List<Spawn>();
            var checkpoint = world.OpenCheckpoint();
            for (int id = 0; id < checkpoint.SlotCount; id++)
            {
                var slot = checkpoint.GetSlot(id);
                if (slot.Alive && checkpoint.TryGet<Projectile>(world.BindEntity(id, slot.Version), out var projectile))
                    spawned.Add(new Spawn(id, slot.Version, projectile));
            }
            return spawned;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
    internal readonly struct Scenario
    {
        public Scenario(int rows, uint seed, int cpuChunk, int gpuChunk, int cpuBudget, int gpuBudget, int workers)
        {
            (Rows, Seed, CpuChunk, GpuChunk) = (rows, seed, cpuChunk, gpuChunk);
            (CpuBudget, GpuBudget, Workers) = (cpuBudget, gpuBudget, workers);
        }

        public int Rows { get; }
        public uint Seed { get; }
        public int CpuChunk { get; }
        public int GpuChunk { get; }
        public int CpuBudget { get; }
        public int GpuBudget { get; }
        public int Workers { get; }

        public override string ToString() =>
            $"rows={Rows}, seed={Seed}, chunks={CpuChunk}/{GpuChunk}, budgets={CpuBudget}/{GpuBudget}, workers={Workers}";
    }

    internal readonly struct Handle
    {
        public Handle(int id, int generation) => (Id, Generation) = (id, generation);
        public int Id { get; }
        public int Generation { get; }
    }

    internal readonly struct Spawn : IEquatable<Spawn>
    {
        public Spawn(int id, int generation, Projectile payload) => (Id, Generation, Payload) = (id, generation, payload);
        public int Id { get; }
        public int Generation { get; }
        public Projectile Payload { get; }

        public bool Equals(Spawn other) => Id == other.Id && Generation == other.Generation && Payload.Equals(other.Payload);
        public override bool Equals(object? obj) => obj is Spawn other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Generation, Payload);
    }

    internal readonly struct OracleRequest
    {
        public OracleRequest(ShotRequest payload, Handle source, int producer, int ordinal) =>
            (Payload, Source, Producer, Ordinal) = (payload, source, producer, ordinal);
        public ShotRequest Payload { get; }
        public Handle Source { get; }
        public int Producer { get; }
        public int Ordinal { get; }
    }

    internal readonly struct OracleDecision
    {
        public OracleDecision(ShotDecision payload, int ordinal) => (Payload, Ordinal) = (payload, ordinal);
        public ShotDecision Payload { get; }
        public int Ordinal { get; }
    }

    internal readonly struct TickEvents
    {
        public TickEvents(OracleRequest[] requests, OracleDecision[] decisions) => (Requests, Decisions) = (requests, decisions);
        public OracleRequest[] Requests { get; }
        public OracleDecision[] Decisions { get; }
    }

    internal readonly struct Totals
    {
        public Totals(long requests, long accepted, long rejected, long heatRejected, long zeroReseeds) =>
            (Requests, Accepted, Rejected, HeatRejected, ZeroReseeds) = (requests, accepted, rejected, heatRejected, zeroReseeds);
        public long Requests { get; }
        public long Accepted { get; }
        public long Rejected { get; }
        public long HeatRejected { get; }
        public long ZeroReseeds { get; }

        public Totals With(Totals other) => new(Requests + other.Requests, Accepted + other.Accepted,
            Rejected + other.Rejected, HeatRejected + other.HeatRejected, ZeroReseeds + other.ZeroReseeds);
    }

    internal sealed class BurstOracle
    {
        private readonly Handle[] sources;

        public BurstOracle(MountRow[] mounts, Handle[] sources)
        {
            Mounts = mounts;
            this.sources = sources;
        }

        public MountRow[] Mounts { get; }
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
}
