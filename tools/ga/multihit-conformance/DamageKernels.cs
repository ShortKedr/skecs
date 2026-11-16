using System;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

public struct DamageFrame
{
    public uint Tick;
}

public enum HitKind
{
    Contact,
    Projectile,
    Debris
}

public enum DamageKind
{
    Neutral,
    Plasma,
    Kinetic
}

public struct Impact
{
    public DamageKind Profile;
    public Fixed Amount;
    public int AttackerId;
    public int AttackerGeneration;
}

public struct HitRequest
{
    public HitKind Kind;
    public int ImpactOrder;
    public int TargetId;
    public int TargetGeneration;
    public Impact Impact;
}

public struct HitSource
{
    public HitRequest Request;
    public uint FirstTick;
    public uint Emissions;
}

public struct ShipDamage
{
    public int Team;
    public bool Alive;
    public Fixed Shield;
    public Fixed Hull;
    public Fixed Armor;
    public Fixed Heat;
    public int LastAttackerId;
    public int LastAttackerGeneration;
    public uint LastDamageTick;
}

public struct AliveShip
{
    public byte Present;
}

public enum HitOutcome
{
    MissingGeneration,
    AlreadyDead,
    Applied,
    Killed
}

public struct HitDecision
{
    public HitKind Kind;
    public DamageKind Profile;
    public int TargetId;
    public int TargetGeneration;
    public int AttackerId;
    public int CreditedAttackerId;
    public int CreditedAttackerGeneration;
    public HitOutcome Outcome;
    public Fixed ShieldLost;
    public Fixed HullLost;
    public Fixed HeatGained;
}

public struct ShipDeath
{
    public int Id;
    public int Generation;
    public int AttackerId;
    public int AttackerGeneration;
}

[SecsSystem(SystemId = "multi-hit-damage")]
public partial struct HitCollection
{
    [GaTickResource(typeof(DamageFrame))]
    public void Execute(in SystemContext context, ref HitSource row)
    {
        ref readonly var frame = ref context.Resource<DamageFrame>();
        if (frame.Tick >= row.FirstTick)
        {
            row.Emissions++;
            context.SetDomainOrder((int)row.Request.Kind, row.Request.ImpactOrder);
            context.Emit(new HitRequest
            {
                Kind = row.Request.Kind,
                ImpactOrder = row.Request.ImpactOrder,
                TargetId = row.Request.TargetId,
                TargetGeneration = row.Request.TargetGeneration,
                Impact = new Impact
                {
                    Profile = row.Request.Impact.Profile,
                    Amount = row.Request.Impact.Amount,
                    AttackerId = row.Request.Impact.AttackerId,
                    AttackerGeneration = row.Request.Impact.AttackerGeneration
                }
            });
        }
    }
}

public static class DamageApplication
{
    public static void Apply(DeterministicOrderedApplyContext<DamageFrame> context)
    {
        var commands = context.Commands;
        var plasmaShieldFactor = Fixed.FromRatio(3, 2);
        var plasmaHeatFactor = Fixed.FromRatio(9, 5);
        foreach (var emission in context.Events)
        {
            var request = emission.Get<HitRequest>();
            var impact = request.Impact;
            var decision = new HitDecision
            {
                Kind = request.Kind,
                Profile = impact.Profile,
                TargetId = request.TargetId,
                TargetGeneration = request.TargetGeneration,
                AttackerId = impact.AttackerId,
                CreditedAttackerId = -1
            };
            if (!context.TryRead<ShipDamage>(request.TargetId, request.TargetGeneration, out var ship))
            {
                decision.Outcome = HitOutcome.MissingGeneration;
                commands.Emit(decision);
                continue;
            }
            if (!ship.Alive)
            {
                decision.Outcome = HitOutcome.AlreadyDead;
                commands.Emit(decision);
                continue;
            }

            bool plasma = impact.Profile == DamageKind.Plasma;
            var shieldFactor = plasma ? plasmaShieldFactor : Fixed.One;
            var hullFactor = impact.Profile == DamageKind.Kinetic ? plasmaShieldFactor : Fixed.One;
            var spentOnShield = Min(impact.Amount, ship.Shield / shieldFactor);
            decision.ShieldLost = Min(ship.Shield, spentOnShield * shieldFactor);
            decision.HullLost = Min(ship.Hull, (impact.Amount - spentOnShield) * hullFactor * (Fixed.One - ship.Armor));
            decision.HeatGained = plasma ? decision.ShieldLost * plasmaHeatFactor : Fixed.Zero;
            ship.Shield -= decision.ShieldLost;
            ship.Hull -= decision.HullLost;
            ship.Heat += decision.HeatGained;
            ship.LastDamageTick = context.Input.Tick;
            bool credit = impact.AttackerId >= 0 &&
                context.TryRead<ShipDamage>(impact.AttackerId, impact.AttackerGeneration, out var attacker) &&
                attacker.Alive && attacker.Team != ship.Team;
            ship.LastAttackerId = credit ? impact.AttackerId : -1;
            ship.LastAttackerGeneration = credit ? impact.AttackerGeneration : 0;
            decision.CreditedAttackerId = ship.LastAttackerId;
            decision.CreditedAttackerGeneration = ship.LastAttackerGeneration;
            ship.Alive = ship.Hull > Fixed.Zero;
            if (!context.TryWrite(request.TargetId, request.TargetGeneration, ship))
                throw new InvalidOperationException("Damage target disappeared during ordered apply.");

            decision.Outcome = ship.Alive ? HitOutcome.Applied : HitOutcome.Killed;
            commands.Emit(decision);
            if (!ship.Alive)
            {
                commands.Remove<AliveShip>(request.TargetId, request.TargetGeneration);
                commands.Emit(new ShipDeath
                {
                    Id = request.TargetId,
                    Generation = request.TargetGeneration,
                    AttackerId = ship.LastAttackerId,
                    AttackerGeneration = ship.LastAttackerGeneration
                });
            }
        }
    }

    private static Fixed Min(Fixed left, Fixed right) => left < right ? left : right;
}
