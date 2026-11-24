#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

namespace CarrierCapacityFixture
{
    internal static class CarrierChecks
    {
        internal static async ValueTask Run(string profile, string lane, bool singleWorker, bool useGpu, bool requireYield,
            Action<DeterministicCommitGroup<CarrierFrame>, Action<GaBatchTimings>> registerCpu,
            Action<DeterministicCommitGroup<CarrierFrame>, Action<GaBatchTimings>>? registerTarget,
            Func<DeterministicRunner<CarrierFrame>, int, ValueTask<bool>> drain, Action<string> report)
        {
            int ticks;
            Scenario[] cases;
            switch (profile)
            {
                case "boundaries":
                    ticks = 6;
                    int[] sizes = { 0, 1, 9, 63, 64, 65, 127, 128, 129, 1024 };
                    uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
                    int[] chunks = { 1, 64, 127, 256 };
                    int[] workers = singleWorker ? new[] { 1 } : new[] { 1, 2, Environment.ProcessorCount };
                    int[] budgets = { 1, 3, 64 };
                    cases = Enumerable.Range(0, sizes.Length * seeds.Length).Select(i => new Scenario(
                        sizes[i / seeds.Length], seeds[i % seeds.Length], chunks[i % chunks.Length],
                        chunks[(i + 1) % chunks.Length], workers[i % workers.Length],
                        budgets[i % budgets.Length], budgets[(i + 1) % budgets.Length])).ToArray();
                    break;
                case "short":
                case "long":
                    ticks = profile == "short" ? 1_000 : 100_000;
                    cases = new Scenario[] { new(17, 7331, 127, 1, singleWorker ? 1 : 2, 3, 64) };
                    break;
                default:
                    throw new ArgumentException($"Unknown profile '{profile}'.");
            }

            var timer = Stopwatch.StartNew();
            var counts = new Coverage();
            foreach (var scenario in cases)
                await RunCase(scenario, ticks, useGpu, requireYield, counts, registerCpu, registerTarget, drain, report);
            Require(counts.Outcomes.All(count => count > 0) && counts.SameTickReceipts > 0 &&
                counts.Retired > 0 && counts.ReusedAfterRetirement > 0 && counts.DomainTies > 0,
                $"The {profile} profile missed a required path.");
            string results = string.Join(" ", Coverage.Results.Select(result => $"{result}={counts.Outcomes[(int)result]}"));
            report($"{lane}: profile={profile} cases={cases.Length} ticks={(long)cases.Length * ticks} " +
                $"requests={counts.Outcomes.Sum()} {results} domainTies={counts.DomainTies} observedBeforeRetire={counts.Visible} " +
                $"sameTickTickets={counts.SameTickReceipts} retired={counts.Retired} reusedAfterRetirement={counts.ReusedAfterRetirement} " +
                $"deviceBatches={counts.GpuBatches} deviceRows={counts.GpuRows} elapsedSeconds={timer.Elapsed.TotalSeconds:F2} allocator=passed");
        }

        private static async ValueTask RunCase(Scenario scenario, int ticks, bool useGpu, bool requireYield, Coverage counts,
            Action<DeterministicCommitGroup<CarrierFrame>, Action<GaBatchTimings>> registerCpu,
            Action<DeterministicCommitGroup<CarrierFrame>, Action<GaBatchTimings>>? registerTarget,
            Func<DeterministicRunner<CarrierFrame>, int, ValueTask<bool>> drain, Action<string> report)
        {
            BuildOrder[] orders = Enumerable.Range(0, scenario.Rows).Select(row => CarrierScenario.MakeOrder(scenario, row)).ToArray();
            var oracle = new CarrierOracle(orders);
            using var cpu = new World(new WorldOptions(scenario.CpuChunk));
            using var target = registerTarget is null ? null : new World(new WorldOptions(scenario.GpuChunk));
            Initialize(cpu, orders);
            if (target != null)
                Initialize(target, orders);

            using var cpuRunner = new DeterministicRunner<CarrierFrame>(cpu, CreateWorkload(registerCpu, _ => { }), scenario.Workers);
            long caseGpuRows = 0;
            using var targetRunner = target == null ? null : new DeterministicRunner<CarrierFrame>(target,
                CreateWorkload(registerTarget!, ObserveBatch));
            string targetLabel = $"{(useGpu ? "GPU" : "CPU fallback")} {scenario}";
            CheckState(cpu, oracle, $"CPU {scenario} initial");
            if (target != null)
                CheckState(target, oracle, $"{targetLabel} initial");

            for (int tick = 1; tick <= ticks; tick++)
            {
                var frame = new CarrierFrame { Tick = tick };
                var expected = oracle.Advance(tick);
                cpuRunner.BeginTick(tick, frame, Fixed.One);
                targetRunner?.BeginTick(tick, frame, Fixed.One);
                await drain(cpuRunner, scenario.CpuBudget);
                CheckEvents(cpuRunner.LastCompleted!, expected, tick, $"CPU {scenario}");
                CheckState(cpu, oracle, $"CPU {scenario} tick={tick}");
                if (targetRunner != null && target != null)
                {
                    bool waited = await drain(targetRunner, scenario.GpuBudget);
                    if (requireYield && useGpu && scenario.Rows != 0 && !waited)
                        throw new InvalidOperationException($"GPU work did not yield at {scenario}, tick {tick}.");
                    Require(caseGpuRows == (useGpu ? (long)scenario.Rows * tick : 0), $"Unexpected device rows at {scenario}, tick {tick}.");
                    CheckEvents(targetRunner.LastCompleted!, expected, tick, targetLabel);
                    CheckState(target, oracle, $"{targetLabel} tick={tick}");
                }

                counts.Count(expected, tick);
                if (ticks >= 1_000 && tick % 1_000 == 0)
                    report($"progress tick={tick}/{ticks} deviceRows={caseGpuRows}");
            }

            void ObserveBatch(GaBatchTimings timing)
            {
                caseGpuRows += timing.Rows;
                counts.GpuRows += timing.Rows;
                counts.GpuBatches++;
            }
        }

        static DeterministicWorkload<CarrierFrame> CreateWorkload(
            Action<DeterministicCommitGroup<CarrierFrame>, Action<GaBatchTimings>> register, Action<GaBatchTimings> observe)
        {
            var stateAccess = new DeterministicQuery().ReadWrite<TeamCapacity>().ReadWrite<HomeCapacity>();
            return new DeterministicWorkload<CarrierFrame>()
                .AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("carrier-team-order", 1), group =>
                {
                    register(group, observe);
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

        private static void Require(bool condition, string context)
        {
            if (!condition)
                throw new InvalidOperationException(context);
        }
    }
    internal sealed class Coverage
    {
        internal static readonly BuildResult[] Results = (BuildResult[])Enum.GetValues(typeof(BuildResult));
        internal readonly long[] Outcomes = new long[Results.Length];
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
}
