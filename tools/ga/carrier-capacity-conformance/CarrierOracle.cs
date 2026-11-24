using System.Collections.Generic;
using System.Linq;
using SKECS;

namespace CarrierCapacityFixture
{
    internal readonly struct Scenario
    {
        internal Scenario(int rows, uint seed, int cpuChunk, int gpuChunk, int workers, int cpuBudget, int gpuBudget)
        {
            (Rows, Seed, CpuChunk, GpuChunk) = (rows, seed, cpuChunk, gpuChunk);
            (Workers, CpuBudget, GpuBudget) = (workers, cpuBudget, gpuBudget);
        }

        internal int Rows { get; }
        internal uint Seed { get; }
        internal int CpuChunk { get; }
        internal int GpuChunk { get; }
        internal int Workers { get; }
        internal int CpuBudget { get; }
        internal int GpuBudget { get; }

        public override string ToString() =>
            $"rows={Rows}, seed={Seed}, chunks={CpuChunk}/{GpuChunk}, budgets={CpuBudget}/{GpuBudget}, workers={Workers}";
    }

    internal readonly struct HomeLimit
    {
        internal HomeLimit(int members, int dockSlots, int repairSlots) => (Members, DockSlots, RepairSlots) = (members, dockSlots, repairSlots);
        internal int Members { get; }
        internal int DockSlots { get; }
        internal int RepairSlots { get; }
    }

    internal readonly struct ExpectedRequest
    {
        internal ExpectedRequest(CarrierAddress source, BuildRequest payload) => (Source, Payload) = (source, payload);
        internal CarrierAddress Source { get; }
        internal BuildRequest Payload { get; }
    }

    internal readonly struct ExpectedDecision
    {
        internal ExpectedDecision(BuildDecision payload, int localOrdinal) => (Payload, LocalOrdinal) = (payload, localOrdinal);
        internal BuildDecision Payload { get; }
        internal int LocalOrdinal { get; }
    }

    internal readonly struct ExpectedVisibility
    {
        internal ExpectedVisibility(CarrierAddress source, ShipVisible payload) => (Source, Payload) = (source, payload);
        internal CarrierAddress Source { get; }
        internal ShipVisible Payload { get; }
    }

    internal sealed class ExpectedTick
    {
        internal ExpectedTick(List<ExpectedRequest> requests, List<ExpectedDecision> decisions, List<ExpectedVisibility> visible) =>
            (Requests, Decisions, Visible) = (requests, decisions, visible);
        internal List<ExpectedRequest> Requests { get; }
        internal List<ExpectedDecision> Decisions { get; }
        internal List<ExpectedVisibility> Visible { get; }
    }

    internal static class CarrierScenario
    {
        internal static readonly int[] TeamLimits = { 5, 2 };
        internal static readonly HomeLimit[] Homes = { new(1, 3, 3), new(8, 2, 1), new(8, 5, 3), new(8, 3, 2) };
        private static readonly OrderShape[] Orders =
        {
            new(0, 0, 0, false),
            new(0, 1, 0, false),
            new(0, 2, 1, true),
            new(0, 3, 1, true),
            new(0, 4, 1, false),
            new(0, 5, 1, false),
            new(1, 0, 2, false),
            new(1, 1, 3, false),
            new(1, 2, 2, true)
        };

        internal static BuildOrder MakeOrder(Scenario scenario, int row)
        {
            int reverse = scenario.Rows - row - 1;
            var shape = Orders[reverse % Orders.Length];
            return new BuildOrder
            {
                Team = shape.Team,
                Order = shape.Order,
                Home = shape.Home,
                Repair = shape.Repair,
                TeamAddress = new CarrierAddress { Id = shape.Team, Generation = 1 },
                HomeAddress = new CarrierAddress { Id = TeamLimits.Length + shape.Home, Generation = 1 },
                Phase = reverse < Orders.Length ? 0 : (int)(((ulong)scenario.Seed + (uint)(reverse / Orders.Length)) % 3)
            };
        }

        private readonly struct OrderShape
        {
            internal OrderShape(int team, int order, int home, bool repair) => (Team, Order, Home, Repair) = (team, order, home, repair);
            internal int Team { get; }
            internal int Order { get; }
            internal int Home { get; }
            internal bool Repair { get; }
        }
    }

    internal sealed class CarrierOracle
    {
        private readonly SortedDictionary<int, HangarShip> _ships = new();
        private readonly List<int> _generations;
        private readonly Stack<int> _free;

        internal CarrierOracle(BuildOrder[] orders)
        {
            Orders = orders;
            int initialCount = CarrierScenario.TeamLimits.Length + CarrierScenario.Homes.Length + orders.Length;
            _generations = Enumerable.Repeat(1, initialCount).Append(2).ToList();
            _free = new Stack<int>(new[] { initialCount });
        }

        internal BuildOrder[] Orders { get; }
        internal IEnumerable<KeyValuePair<int, HangarShip>> Ships => _ships;
        internal int SlotCount => _generations.Count;
        internal IEnumerable<int> FreeIds => _free;
        internal (int Generation, bool Alive) Slot(int id) => (_generations[id], !_free.Contains(id));

        internal ExpectedTick Advance(int tick)
        {
            int sourceOffset = CarrierScenario.TeamLimits.Length + CarrierScenario.Homes.Length;
            var requests = new List<ExpectedRequest>();
            for (int row = 0; row < Orders.Length; row++)
            {
                ref var order = ref Orders[row];
                if (order.Phase != 0 && (tick + order.Phase) % 3 == 0)
                    continue;
                order.Submitted += 1;
                requests.Add(new ExpectedRequest(new CarrierAddress { Id = sourceOffset + row, Generation = 1 },
                    new BuildRequest
                    {
                        Team = order.Team,
                        Order = order.Order,
                        Home = order.Home,
                        Repair = order.Repair,
                        TeamAddress = order.TeamAddress,
                        HomeAddress = order.HomeAddress
                    }));
            }

            requests = requests.OrderBy(entry => entry.Payload.Team).ThenBy(entry => entry.Payload.Order)
                .ThenBy(entry => entry.Source.Id).ToList();
            var decisions = new List<ExpectedDecision>();
            int ordinal = 0;
            foreach (var entry in requests)
            {
                var request = entry.Payload;
                var home = CarrierScenario.Homes[request.Home];
                var homeShips = _ships.Values.Where(ship => ship.Home == request.Home).ToArray();
                var openDocks = Enumerable.Range(0, home.DockSlots).Except(homeShips.Select(ship => ship.Dock));
                int dock = openDocks.DefaultIfEmpty(-1).First();
                var result = _ships.Values.Count(ship => ship.Team == request.Team) == CarrierScenario.TeamLimits[request.Team]
                    ? BuildResult.TeamLimit
                    : homeShips.Length == home.Members
                        ? BuildResult.HomeLimit
                        : dock == -1
                            ? BuildResult.DockLimit
                            : request.Repair && homeShips.Count(ship => ship.Repair) == home.RepairSlots
                                ? BuildResult.RepairLimit
                                : BuildResult.Accepted;

                if (result == BuildResult.Accepted)
                {
                    _ships.Add(Allocate(), new HangarShip
                    {
                        RequestId = entry.Source.Id,
                        RequestGeneration = entry.Source.Generation,
                        Team = request.Team,
                        Order = request.Order,
                        Home = request.Home,
                        Dock = dock,
                        BuiltAt = tick,
                        Repair = request.Repair,
                        TeamAddress = request.TeamAddress,
                        HomeAddress = request.HomeAddress
                    });
                    ordinal += 2;
                }
                decisions.Add(new ExpectedDecision(new BuildDecision
                {
                    SourceId = entry.Source.Id,
                    SourceGeneration = entry.Source.Generation,
                    Team = request.Team,
                    Order = request.Order,
                    Home = request.Home,
                    Dock = result == BuildResult.Accepted ? dock : -1,
                    Repair = request.Repair,
                    Result = result
                }, ordinal++));
            }

            var visible = _ships.Select(item => new ExpectedVisibility(
                new CarrierAddress { Id = item.Key, Generation = _generations[item.Key] },
                new ShipVisible { Ship = item.Value, Retire = item.Value.BuiltAt <= tick - 2 })).ToList();
            foreach (var item in visible.Where(item => item.Payload.Retire))
            {
                int id = item.Source.Id;
                _ships.Remove(id);
                _generations[id]++;
                _free.Push(id);
            }

            return new ExpectedTick(requests, decisions, visible);
        }

        private int Allocate()
        {
            if (_free.TryPop(out int id))
                return id;
            _generations.Add(1);
            return _generations.Count - 1;
        }
    }
}
