#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SKECS;
using SKECS.Numerics;

namespace MultiHitFixture
{
    internal sealed class DamageOracle
    {
        private const long One = 1L << 32;
        private const long ThreeHalves = One + One / 2;
        private const long PlasmaHeatRate = 7_730_941_133;
        private readonly ShipDamage[] _ships;
        private readonly Entity[] _shipHandles;
        private readonly OrderedHit[] _orderedHits;

        public DamageOracle(ShipDamage[] ships, Entity[] shipHandles, HitSource[] rows, Entity[] sources)
        {
            _ships = (ShipDamage[])ships.Clone();
            _shipHandles = shipHandles;
            _orderedHits = rows.Select((row, index) => new OrderedHit(row.Request, row.FirstTick, sources[index]))
                .OrderBy(hit => hit.Request.Kind)
                .ThenBy(hit => hit.Request.ImpactOrder)
                .ThenBy(hit => hit.Source.Id)
                .ThenBy(hit => hit.Source.Version)
                .ToArray();
        }

        public Coverage Coverage { get; private set; }

        public ShipDamage ShipAt(int index) => _ships[index];

        public int RestoreTargets(ShipDamage[] startingShips)
        {
            int revivals = 0;
            for (int i = 2; i < _ships.Length; i++)
            {
                if (!_ships[i].Alive)
                    revivals++;
                _ships[i] = startingShips[i];
            }
            return revivals;
        }

        public List<ExpectedEvent> Advance(uint tick)
        {
            var requests = new List<ExpectedEvent>();
            var decisions = new List<ExpectedEvent>();
            var coverage = Coverage;
            int ordinal = 0;
            DeterministicDomainKey? previous = null;
            foreach (var hit in _orderedHits)
            {
                if (hit.FirstTick > tick)
                    continue;
                var request = hit.Request;
                var domain = new DeterministicDomainKey((int)request.Kind, request.ImpactOrder);
                requests.Add(new ExpectedEvent(request, hit.Source.Id, hit.Source.Version, 0, domain));
                coverage.Hits++;
                if (Nullable.Equals(previous, domain))
                    coverage.Ties++;
                previous = domain;

                var decision = new HitDecision
                {
                    Kind = request.Kind,
                    Profile = request.Impact.Profile,
                    TargetId = request.TargetId,
                    TargetGeneration = request.TargetGeneration,
                    AttackerId = request.Impact.AttackerId,
                    CreditedAttackerId = -1
                };
                int target = request.TargetId;
                if (_shipHandles[target].Version != request.TargetGeneration)
                {
                    decision.Outcome = HitOutcome.MissingGeneration;
                    coverage.Stale++;
                }
                else
                {
                    ref var ship = ref _ships[target];
                    if (!ship.Alive)
                    {
                        decision.Outcome = HitOutcome.AlreadyDead;
                        coverage.Dead++;
                        if (ship.LastDamageTick == tick)
                            coverage.SameTickDeath++;
                    }
                    else
                    {
                        Resolve(request.Impact, ref ship, ref decision);
                        ship.LastDamageTick = tick;
                        if (decision.HeatGained.Raw > 0)
                            coverage.Heat++;
                        if (decision.CreditedAttackerId >= 0)
                            coverage.Credited++;
                        else if (request.Impact.AttackerId >= 0 && !_ships[request.Impact.AttackerId].Alive &&
                            _ships[request.Impact.AttackerId].Team != ship.Team)
                            coverage.DeadAttackers++;
                        if (ship.Alive)
                        {
                            coverage.Applied++;
                            decision.Outcome = HitOutcome.Applied;
                        }
                        else
                        {
                            coverage.Killed++;
                            decision.Outcome = HitOutcome.Killed;
                        }
                    }
                }

                decisions.Add(new ExpectedEvent(decision, 0, 0, ordinal++, null));
                if (decision.Outcome == HitOutcome.Killed)
                {
                    var death = new ShipDeath
                    {
                        Id = target,
                        Generation = request.TargetGeneration,
                        AttackerId = decision.CreditedAttackerId,
                        AttackerGeneration = decision.CreditedAttackerGeneration
                    };
                    ordinal++; // The queued removal of the AliveShip component uses its own ordinal.
                    decisions.Add(new ExpectedEvent(death, 0, 0, ordinal++, null));
                }
            }
            Coverage = coverage;
            requests.AddRange(decisions);
            return requests;
        }

        private void Resolve(Impact impact, ref ShipDamage ship, ref HitDecision decision)
        {
            long shield = ship.Shield.Raw;
            long hull = ship.Hull.Raw;
            long baseDamage = impact.Amount.Raw;
            long plasmaHeat = 0;
            long hullBeforeArmor;
            switch (impact.Profile)
            {
                case DamageKind.Plasma:
                    long shieldBudget = Math.Min(baseDamage, Round((BigInteger)shield * 2, 3));
                    shield -= Math.Min(shield, Round((BigInteger)shieldBudget * 3, 2));
                    plasmaHeat = Round((BigInteger)(ship.Shield.Raw - shield) * PlasmaHeatRate, One);
                    hullBeforeArmor = baseDamage - shieldBudget;
                    break;
                case DamageKind.Kinetic:
                    shield = Math.Max(0, shield - baseDamage);
                    hullBeforeArmor = Round((BigInteger)Math.Max(0, baseDamage - ship.Shield.Raw) * ThreeHalves, One);
                    break;
                default:
                    shield = Math.Max(0, shield - baseDamage);
                    hullBeforeArmor = Math.Max(0, baseDamage - ship.Shield.Raw);
                    break;
            }
            hull = Math.Max(0, hull - Round((BigInteger)hullBeforeArmor * (One - ship.Armor.Raw), One));
            decision.ShieldLost = Fixed.FromRaw(ship.Shield.Raw - shield);
            decision.HullLost = Fixed.FromRaw(ship.Hull.Raw - hull);
            decision.HeatGained = Fixed.FromRaw(plasmaHeat);

            int credited = impact.AttackerId;
            if (credited < 0 || _shipHandles[credited].Version != impact.AttackerGeneration ||
                _ships[credited].Team == ship.Team || !_ships[credited].Alive)
                credited = -1;
            decision.CreditedAttackerId = credited;
            decision.CreditedAttackerGeneration = credited >= 0 ? _shipHandles[credited].Version : 0;
            ship.LastAttackerId = credited;
            ship.LastAttackerGeneration = decision.CreditedAttackerGeneration;
            ship.Shield = Fixed.FromRaw(shield);
            ship.Hull = Fixed.FromRaw(hull);
            ship.Heat = Fixed.FromRaw(ship.Heat.Raw + plasmaHeat);
            ship.Alive = hull != 0;
        }

        private static long Round(BigInteger numerator, long denominator)
        {
            var whole = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 > denominator || (remainder * 2 == denominator && !whole.IsEven))
                whole++;
            return (long)whole;
        }

        private readonly struct OrderedHit
        {
            internal OrderedHit(HitRequest request, uint firstTick, Entity source) =>
                (Request, FirstTick, Source) = (request, firstTick, source);
            internal HitRequest Request { get; }
            internal uint FirstTick { get; }
            internal Entity Source { get; }
        }
    }

    internal readonly struct ExpectedEvent
    {
        internal ExpectedEvent(object payload, int sourceId, int sourceGeneration, int ordinal, DeterministicDomainKey? domain) =>
            (Payload, SourceId, SourceGeneration, Ordinal, Domain) = (payload, sourceId, sourceGeneration, ordinal, domain);
        public object Payload { get; }
        public int SourceId { get; }
        public int SourceGeneration { get; }
        public int Ordinal { get; }
        public DeterministicDomainKey? Domain { get; }
    }

    internal struct Coverage
    {
        public int Hits;
        public int Applied;
        public int Killed;
        public int Dead;
        public int Stale;
        public int Heat;
        public int Credited;
        public int DeadAttackers;
        public int SameTickDeath;
        public int Ties;

        public static Coverage operator +(Coverage left, Coverage right) => new()
        {
            Hits = left.Hits + right.Hits,
            Applied = left.Applied + right.Applied,
            Killed = left.Killed + right.Killed,
            Dead = left.Dead + right.Dead,
            Stale = left.Stale + right.Stale,
            Heat = left.Heat + right.Heat,
            Credited = left.Credited + right.Credited,
            DeadAttackers = left.DeadAttackers + right.DeadAttackers,
            SameTickDeath = left.SameTickDeath + right.SameTickDeath,
            Ties = left.Ties + right.Ties
        };

        public static Coverage operator -(Coverage left, Coverage right) => new()
        {
            Hits = left.Hits - right.Hits,
            Applied = left.Applied - right.Applied,
            Killed = left.Killed - right.Killed,
            Dead = left.Dead - right.Dead,
            Stale = left.Stale - right.Stale,
            Heat = left.Heat - right.Heat,
            Credited = left.Credited - right.Credited,
            DeadAttackers = left.DeadAttackers - right.DeadAttackers,
            SameTickDeath = left.SameTickDeath - right.SameTickDeath,
            Ties = left.Ties - right.Ties
        };
    }
}
