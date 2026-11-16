using SKECS;

internal readonly record struct Scenario(int Rows, uint Seed, int CpuChunk, int GpuChunk, int Workers, int CpuBudget, int GpuBudget);
internal readonly record struct HomeLimit(int Members, int DockSlots, int RepairSlots);
internal readonly record struct OrderShape(int Team, int Order, int Home, bool Repair);
internal readonly record struct ExpectedRequest(CarrierAddress Source, BuildRequest Payload);
internal readonly record struct ExpectedDecision(BuildDecision Payload, int LocalOrdinal);
internal readonly record struct ExpectedVisibility(CarrierAddress Source, ShipVisible Payload);
internal sealed record ExpectedTick(List<ExpectedRequest> Requests, List<ExpectedDecision> Decisions, List<ExpectedVisibility> Visible);

internal static class CarrierScenario
{
    internal static readonly int[] TeamLimits = [5, 2];
    internal static readonly HomeLimit[] Homes = [new(1, 3, 3), new(8, 2, 1), new(8, 5, 3), new(8, 3, 2)];
    private static readonly OrderShape[] Orders =
    [
        new(0, 0, 0, false),
        new(0, 1, 0, false),
        new(0, 2, 1, true),
        new(0, 3, 1, true),
        new(0, 4, 1, false),
        new(0, 5, 1, false),
        new(1, 0, 2, false),
        new(1, 1, 3, false),
        new(1, 2, 2, true)
    ];

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
}

internal sealed class CarrierOracle(BuildOrder[] orders)
{
    private readonly SortedDictionary<int, HangarShip> _ships = new();
    private readonly List<int> _generations = Enumerable.Repeat(1, CarrierScenario.TeamLimits.Length + CarrierScenario.Homes.Length + orders.Length)
        .Append(2).ToList();
    private readonly Stack<int> _free = new([CarrierScenario.TeamLimits.Length + CarrierScenario.Homes.Length + orders.Length]);

    internal BuildOrder[] Orders { get; } = orders;
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
            int dock = openDocks.FirstOrDefault(-1);
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
