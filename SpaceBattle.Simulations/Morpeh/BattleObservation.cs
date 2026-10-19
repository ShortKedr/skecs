using System.Numerics;

namespace SpaceBattle.Simulations.Morpeh;

public sealed partial class Battle
{
    // Presentation-only watch state: not a component, RNG input or part of the replay checksum.
    private int observedShipSerial = -1;
    private ObservedShipDeath observedDeath = new(-1, -1, Vector2.Zero, -1);
    public ObservedShipDeath ObservedDeath => observedDeath;

    public void ObserveShip(int serial)
    {
        observedShipSerial = serial;
        observedDeath = new(-1, -1, Vector2.Zero, -1);
    }

    public bool TryGetShipObservation(int serial, out ShipObservation observation)
    {
        observation = default;
        if (!serialToShip.TryGetValue(serial, out var e) || !AliveShip(e)) return false;
        var id = Store.Get<Identity>(e); var t = Store.Get<Transform>(e);
        var hull = Store.Get<Hull>(e); var shield = Store.Get<Shield>(e); var heat = Store.Get<Heat>(e);
        var dockCarrier = Store.Has<Docked>(e) ? Serial(Store.Get<Docked>(e).Carrier) : -1;
        var target = dockCarrier >= 0 ? -1 : Serial(Store.Get<Brain>(e).Target);
        observation = new(id.Serial, id.Team, (int)id.Class, t.Position, Store.Get<Collider>(e).Radius,
            Ratio(hull.Current, hull.Maximum), Ratio(shield.Current, shield.Maximum), Ratio(heat.Value, heat.Limit), target, dockCarrier);
        return true;

        int Serial(EntityKey entity) => AliveShip(entity) ? Store.Get<Identity>(entity).Serial : -1;
        static float Ratio(float value, float maximum) => maximum > 0 ? value / maximum : 0;
    }

    private void RecordObservedDeath(EntityKey victim, EntityKey attacker)
    {
        if (observedShipSerial < 0 || Store.Get<Identity>(victim).Serial != observedShipSerial || observedDeath.Serial >= 0) return;
        // A fatal projectile's actual owner is useful even for friendly fire or a now-dead shooter.
        var killer = Store.Alive(attacker) && Store.Has<Identity>(attacker)
            ? Store.Get<Identity>(attacker).Serial : -1;
        observedDeath = new(observedShipSerial, killer, Store.Get<Transform>(victim).Position, Tick);
    }
}
