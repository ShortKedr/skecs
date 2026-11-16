using System;
using SKECS;
using SKECS.GA;

public struct CarrierFrame
{
    public int Tick;
}

public struct CarrierAddress
{
    public int Id;
    public int Generation;
}

public struct BuildOrder
{
    public int Team;
    public int Order;
    public int Home;
    public int Phase;
    public bool Repair;
    public CarrierAddress TeamAddress;
    public CarrierAddress HomeAddress;
    public int Submitted;
}

public struct BuildRequest
{
    public int Team;
    public int Order;
    public int Home;
    public bool Repair;
    public CarrierAddress TeamAddress;
    public CarrierAddress HomeAddress;
}

public enum BuildResult
{
    Accepted,
    TeamLimit,
    HomeLimit,
    DockLimit,
    RepairLimit
}

public struct BuildDecision
{
    public int SourceId;
    public int SourceGeneration;
    public int Team;
    public int Order;
    public int Home;
    public int Dock;
    public bool Repair;
    public BuildResult Result;
}

public struct TeamCapacity
{
    public int Limit;
    public int Members;
}

public struct HomeCapacity
{
    public int Limit;
    public int DockSlots;
    public int RepairSlots;
    public int Members;
    public int OccupiedDocks;
    public int Repairing;
}

public struct HangarShip
{
    public int RequestId;
    public int RequestGeneration;
    public int Team;
    public int Order;
    public int Home;
    public int Dock;
    public int BuiltAt;
    public bool Repair;
    public CarrierAddress TeamAddress;
    public CarrierAddress HomeAddress;
}

public struct ShipVisible
{
    public HangarShip Ship;
    public bool Retire;
}

[SecsSystem(SystemId = "carrier-capacity")]
public partial struct CarrierOrders
{
    [GaTickResource(typeof(CarrierFrame))]
    public void Execute(in SystemContext context, ref BuildOrder order)
    {
        ref readonly var frame = ref context.Resource<CarrierFrame>();
        if (order.Phase == 0 || (frame.Tick + order.Phase) % 3 != 0)
        {
            order.Submitted++;
            context.SetDomainOrder(order.Team, order.Order);
            context.Emit(new BuildRequest
            {
                Team = order.Team,
                Order = order.Order,
                Home = order.Home,
                Repair = order.Repair,
                TeamAddress = new CarrierAddress { Id = order.TeamAddress.Id, Generation = order.TeamAddress.Generation },
                HomeAddress = new CarrierAddress { Id = order.HomeAddress.Id, Generation = order.HomeAddress.Generation }
            });
        }
    }
}

internal static class CarrierApply
{
    internal static void Build(DeterministicOrderedApplyContext<CarrierFrame> context)
    {
        var commands = context.Commands;
        foreach (var emission in context.Events)
        {
            var request = emission.Get<BuildRequest>();
            var team = request.TeamAddress;
            var home = request.HomeAddress;
            if (!context.TryRead<TeamCapacity>(team.Id, team.Generation, out var fleet) ||
                !context.TryRead<HomeCapacity>(home.Id, home.Generation, out var hangar))
                throw new InvalidOperationException("An order references a missing team or home.");

            int dock = -1;
            BuildResult result;
            if (fleet.Members >= fleet.Limit)
                result = BuildResult.TeamLimit;
            else if (hangar.Members >= hangar.Limit)
                result = BuildResult.HomeLimit;
            else
            {
                dock = FirstAvailableDock(hangar);
                if (dock < 0)
                    result = BuildResult.DockLimit;
                else if (request.Repair && hangar.Repairing >= hangar.RepairSlots)
                    result = BuildResult.RepairLimit;
                else
                {
                    result = BuildResult.Accepted;
                    fleet.Members++;
                    hangar.Members++;
                    hangar.OccupiedDocks |= 1 << dock;
                    if (request.Repair)
                        hangar.Repairing++;
                    if (!context.TryWrite(team.Id, team.Generation, fleet) ||
                        !context.TryWrite(home.Id, home.Generation, hangar))
                        throw new InvalidOperationException("A declared capacity disappeared during build.");
                    var ticket = commands.Create();
                    commands.Add(ticket, new HangarShip
                    {
                        RequestId = emission.Key.SourceId,
                        RequestGeneration = emission.Key.SourceGeneration,
                        Team = request.Team,
                        Order = request.Order,
                        Home = request.Home,
                        Dock = dock,
                        BuiltAt = context.Input.Tick,
                        Repair = request.Repair,
                        TeamAddress = team,
                        HomeAddress = home
                    });
                }
            }
            commands.Emit(new BuildDecision
            {
                SourceId = emission.Key.SourceId,
                SourceGeneration = emission.Key.SourceGeneration,
                Team = request.Team,
                Order = request.Order,
                Home = request.Home,
                Dock = result == BuildResult.Accepted ? dock : -1,
                Repair = request.Repair,
                Result = result
            });
        }
    }

    internal static void Retire(DeterministicOrderedApplyContext<CarrierFrame> context)
    {
        foreach (var emission in context.Events)
        {
            var visible = emission.Get<ShipVisible>();
            if (!visible.Retire)
                continue;
            var ship = visible.Ship;
            var team = ship.TeamAddress;
            var home = ship.HomeAddress;
            if (!context.TryRead<TeamCapacity>(team.Id, team.Generation, out var fleet) ||
                !context.TryRead<HomeCapacity>(home.Id, home.Generation, out var hangar))
                throw new InvalidOperationException("A ship references a missing team or home.");
            fleet.Members--;
            hangar.Members--;
            hangar.OccupiedDocks &= ~(1 << ship.Dock);
            if (ship.Repair)
                hangar.Repairing--;
            if (!context.TryWrite(team.Id, team.Generation, fleet) ||
                !context.TryWrite(home.Id, home.Generation, hangar))
                throw new InvalidOperationException("A declared capacity disappeared during retirement.");
            context.Commands.Destroy(emission.Key.SourceId, emission.Key.SourceGeneration);
        }
    }

    private static int FirstAvailableDock(HomeCapacity home)
    {
        for (int slot = 0; slot < home.DockSlots; slot++)
            if ((home.OccupiedDocks & (1 << slot)) == 0)
                return slot;
        return -1;
    }
}

internal readonly struct ObserveCommittedShips : IDeterministicJobProcessor<CarrierFrame>
{
    public void Execute(DeterministicJob<CarrierFrame> job)
    {
        for (int index = 0; index < job.Count; index++)
        {
            var ship = job.Read<HangarShip>(index);
            job.ForEntity(index).Emit(new ShipVisible
            {
                Ship = ship,
                Retire = job.Input.Tick - ship.BuiltAt >= 2
            });
        }
    }
}
