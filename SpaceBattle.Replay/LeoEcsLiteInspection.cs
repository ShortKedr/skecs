namespace SpaceBattle;

// Inspection and validity decisions are software-side, outside simulation cores.
public static class LeoEcsLiteInspection
{
    public static ulong Checksum(this Simulations.LeoEcsLite.Battle battle)
        => new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics).Capture();

    public static void Validate(this Simulations.LeoEcsLite.Battle battle)
    {
        var entities = new List<EntityKey>();
        battle.CopyEntityKeys(entities);
        var census = battle.Store.Diagnostics();
        if (census.Entities != entities.Count || battle.PhysicsBodies != battle.ShipCount || battle.Counters.SelfHits != 0)
            throw new InvalidOperationException($"{battle.Framework}: invalid simulation census or self hit at tick {battle.Tick}");
        var seen = new HashSet<int>();
        var ships = new List<EntityKey>();
        var mounts = new Dictionary<EntityKey, int>();
        foreach (var e in entities)
        {
            if (!battle.Store.Alive(e)) throw new InvalidOperationException("Invalid entity lifetime.");
            var t = battle.Store.Get<Transform>(e);
            if (battle.Store.Has<Identity>(e))
            {
                var id = battle.Store.Get<Identity>(e);
                if (!seen.Add(id.Serial)) throw new InvalidOperationException("Duplicate ship serial.");
                ships.Add(e);
                var hull = battle.Store.Get<Hull>(e); var shield = battle.Store.Get<Shield>(e); var heat = battle.Store.Get<Heat>(e);
                var inventory = battle.Store.Get<Inventory>(e); var motion = battle.Store.Get<Motion>(e);
                var collider = battle.Store.Get<Collider>(e); var brain = battle.Store.Get<Brain>(e);
                if (!float.IsFinite(t.Position.X + t.Position.Y + t.Angle + motion.Velocity.X + motion.Velocity.Y + heat.Value +
                    hull.Current + hull.Maximum + hull.Armor + shield.Current + shield.Maximum + shield.RegenPerSecond +
                    heat.Limit + heat.CoolingPerSecond + brain.EstimatedKillSeconds + motion.AngularVelocity) ||
                    brain.EstimatedKillSeconds < 0 || hull.Current < 0 || hull.Current > hull.Maximum || hull.Maximum <= 0 ||
                    shield.Current < 0 || shield.Current > shield.Maximum || shield.Maximum <= 0 || heat.Value < 0 || heat.Limit <= 0 ||
                    inventory.Capacity is < 1 or > 16 || collider.ShapeCount != ShipCatalog.Shapes(id.Class))
                    throw new InvalidOperationException($"Invalid ship state: {id.Serial}");
                for (var i = 0; i < inventory.Capacity; i++)
                    if (inventory.Items[i].Count < 0) throw new InvalidOperationException("Negative inventory.");
            }
            else if (battle.Store.Has<MountedTurret>(e))
            {
                var mount = battle.Store.Get<MountedTurret>(e); var heat = battle.Store.Get<Heat>(e);
                if (!AliveShip(mount.Parent) || !seen.Add(mount.Serial) ||
                    !float.IsFinite(t.Position.X + t.Position.Y + t.Angle + heat.Value + heat.Limit + mount.LocalOffset.X + mount.LocalOffset.Y) ||
                    heat.Value < 0 || heat.Limit <= 0 || mount.Slot < 0 || mount.Slot >= ShipCatalog.TurretCount(battle.Store.Get<Identity>(mount.Parent).Class))
                    throw new InvalidOperationException("Invalid turret attachment.");
                mounts.TryGetValue(mount.Parent, out var count); mounts[mount.Parent] = count + 1;
            }
            else
            {
                var serial = battle.Store.Has<Projectile>(e) ? battle.Store.Get<Projectile>(e).Serial
                    : battle.Store.Has<Debris>(e) ? battle.Store.Get<Debris>(e).Serial : battle.Store.Get<Particle>(e).Serial;
                var motion = battle.Store.Get<Motion>(e);
                if (!seen.Add(serial) || !float.IsFinite(t.Position.X + t.Position.Y + motion.Velocity.X + motion.Velocity.Y))
                    throw new InvalidOperationException("Invalid transient entity.");
            }
        }
        foreach (var e in ships)
        {
            mounts.TryGetValue(e, out var count);
            if (count != ShipCatalog.TurretCount(battle.Store.Get<Identity>(e).Class))
                throw new InvalidOperationException("Missing turret attachment.");
        }
        bool AliveShip(EntityKey e) => battle.Store.Alive(e) && battle.Store.Has<Identity>(e);
    }
}
