#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

namespace MultiHitFixture
{
    internal static class MultiHitScenario
    {
        internal static async ValueTask RunSuite(string lane, string profile, bool singleWorker, bool useGpu,
            Action<DeterministicCommitGroup<DamageFrame>, Action<GaBatchTimings>> registerCpu,
            Action<DeterministicCommitGroup<DamageFrame>, Action<GaBatchTimings>>? registerTarget,
            Func<DeterministicRunner<DamageFrame>, int, bool, ValueTask<DeterministicTickStatus>> drain,
            Action<string> report, Action<object>? reportMismatch = null)
        {
            var (cases, ticks) = SelectCases(profile, singleWorker);
            var total = new Coverage();
            var finalPeriod = new Coverage();
            long revivals = 0;
            long deviceBatches = 0;
            long deviceRows = 0;
            foreach (var scenario in cases)
            {
                var expected = await RunCase(scenario, ticks, onGpu: false, "CPU", registerCpu, drain, report, reportMismatch);
                total += expected.Counters;
                finalPeriod += expected.FinalPeriod;
                revivals += expected.Revivals;
                if (registerTarget is not null)
                {
                    var observed = await RunCase(scenario, ticks, useGpu, useGpu ? "GPU" : "CPU fallback",
                        registerTarget, drain, report, reportMismatch);
                    Check(observed.Counters.Equals(expected.Counters) && observed.FinalPeriod.Equals(expected.FinalPeriod) &&
                        observed.Revivals == expected.Revivals, "Coverage differs between reference and target routes.");
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
            report($"{lane}: profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
                $"sizeBoundaries={string.Join(",", cases.Select(item => item.Rows).Distinct().OrderBy(value => value))} " +
                $"seeds={string.Join(",", cases.Select(item => item.Seed).Distinct().OrderBy(value => value))} " +
                $"workers={string.Join(",", cases.Select(item => item.CpuWorkers).Distinct().OrderBy(value => value))} " +
                $"chunks={string.Join(",", cases.SelectMany(item => new[] { item.CpuChunk, item.GpuChunk }).Distinct().OrderBy(value => value))} " +
                $"budgets={string.Join(",", cases.SelectMany(item => new[] { item.CpuBudget, item.GpuBudget }).Distinct().OrderBy(value => value))} hits={total.Hits} " +
                $"applied={total.Applied} deaths={total.Killed} deadRejected={total.Dead} sameTickDeathRejected={total.SameTickDeath} " +
                $"staleRejected={total.Stale} credited={total.Credited} deadAttackerRejected={total.DeadAttackers} " +
                $"plasmaHeat={total.Heat} domainTies={total.Ties} events={total.Hits * 2 + total.Killed} " +
                $"revivals={revivals} finalPeriodHeat={finalPeriod.Heat} finalPeriodDeaths={finalPeriod.Killed} " +
                $"finalPeriodDeadAttackerRejected={finalPeriod.DeadAttackers} deviceBatches={deviceBatches} deviceRows={deviceRows} " +
                "requestAndDecisionKeys=passed exactShipState=passed aliveTag=passed singleDeath=passed");
        }

        private static (Scenario[] Cases, int Ticks) SelectCases(string profile, bool singleWorker)
        {
            if (profile is "short" or "long")
                return (new Scenario[] { new(65, 27, 127, 64, singleWorker ? 1 : 2, 3, 64) }, profile == "short" ? 1_000 : 100_000);
            if (profile == "smoke")
                return (new Scenario[] {
                    new(0, 0, 1, 3, 1, 1, 1),
                    new(1, 0, 64, 17, singleWorker ? 1 : 2, 3, 1),
                    new(63, 1, 127, 64, singleWorker ? 1 : 2, 1, 3),
                    new(64, 27, 17, 256, 1, 3, 1),
                    new(65, 7331, 256, 7, singleWorker ? 1 : 2, 64, 3),
                    new(127, uint.MaxValue, 63, 127, 1, 1, 64),
                    new(128, 27, 7, 63, singleWorker ? 1 : 2, 3, 1),
                    new(129, 0, 31, 67, 1, 3, 1),
                    new(1024, uint.MaxValue, 128, 257, singleWorker ? 1 : 2, 64, 3)
                }, 6);
            int[] sizes = { 0, 1, 63, 64, 65, 127, 128, 129, 1024 };
            uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
            int[] chunks = { 1, 64, 127, 256 };
            int[] workers = singleWorker ? new[] { 1 } : new[] { 1, 2, Environment.ProcessorCount };
            int[] budgets = { 1, 3, 64 };
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

        private static async ValueTask<CaseResult> RunCase(Scenario scenario, int ticks, bool onGpu, string executor,
            Action<DeterministicCommitGroup<DamageFrame>, Action<GaBatchTimings>> register,
            Func<DeterministicRunner<DamageFrame>, int, bool, ValueTask<DeterministicTickStatus>> drain,
            Action<string> report, Action<object>? reportMismatch)
        {
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
                    register(group, ObserveDeviceBatch);
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
                var status = await drain(runner, budget, onGpu);
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
                catch (InvalidOperationException error) when (reportMismatch is not null)
                {
                    reportMismatch(new
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
                    });
                    throw;
                }
                if (ticks >= 1_000 && tick % 1_000 == 0)
                    report($"progress lane={executor} rows={scenario.Rows} seed={scenario.Seed} tick={tick}/{ticks} " +
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

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
    internal readonly struct Scenario
    {
        internal Scenario(int rows, uint seed, int cpuChunk, int gpuChunk, int cpuWorkers, int cpuBudget, int gpuBudget)
        {
            (Rows, Seed, CpuChunk, GpuChunk) = (rows, seed, cpuChunk, gpuChunk);
            (CpuWorkers, CpuBudget, GpuBudget) = (cpuWorkers, cpuBudget, gpuBudget);
        }

        internal int Rows { get; }
        internal uint Seed { get; }
        internal int CpuChunk { get; }
        internal int GpuChunk { get; }
        internal int CpuWorkers { get; }
        internal int CpuBudget { get; }
        internal int GpuBudget { get; }

        public override string ToString() =>
            $"rows={Rows}, seed={Seed}, chunks={CpuChunk}/{GpuChunk}, budgets={CpuBudget}/{GpuBudget}, workers={CpuWorkers}";
    }

    internal readonly struct CaseResult
    {
        internal CaseResult(Coverage counters, Coverage finalPeriod, long revivals, long deviceBatches, long deviceRows) =>
            (Counters, FinalPeriod, Revivals, DeviceBatches, DeviceRows) = (counters, finalPeriod, revivals, deviceBatches, deviceRows);
        internal Coverage Counters { get; }
        internal Coverage FinalPeriod { get; }
        internal long Revivals { get; }
        internal long DeviceBatches { get; }
        internal long DeviceRows { get; }
    }
}
