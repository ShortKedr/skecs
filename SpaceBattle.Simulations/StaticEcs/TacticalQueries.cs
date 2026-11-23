using System.Runtime.CompilerServices;
using NativeWorld = FFS.Libraries.StaticEcs.World<SpaceBattle.Simulations.StaticEcs.NativeWorldType>;
using System.Numerics;

namespace SpaceBattle.Simulations.StaticEcs;

public sealed partial class Battle
{
    private readonly List<int> coverCandidates = new(68);
    private float maximumTargetSpeed;
    private readonly record struct FireIntent(int Serial, int Target, float Shield, float Hull);
    private readonly List<FireIntent> intents = new();

    private SpatialPointIndex MemberPoints(int team) => team == 0 ? teamPoints0 : team == 1 ? teamPoints1 : tacticalPoints;
    private SpatialPointIndex OpponentPoints(int team) => team == 0 ? teamPoints1 : team == 1 ? teamPoints0 : tacticalPoints;

    private void QueryTactical(SpatialPointIndex points, Vector2 position, float radius, List<int> output, int limit,
        ReadOnlySpan<bool> excluded = default, ReadOnlySpan<bool> ineligible = default)
    {
        var before = spatialWork;
        points.QueryLimited(position, radius, output, limit, ref spatialWork, excluded, ineligible);
        Tactics.SpatialWork += before - spatialWork;
        if (spatialWork == 0) Tactics.BudgetFallbacks++;
    }

    private void FillTargetCandidates(int actorIndex)
    {
        ref readonly var actor = ref AiRows[actorIndex];
        var radius = (double)OperatingRange(actor) + Config.Tactics.MaximumEngageSeconds * ((double)actor.Motion.MaxSpeed + maximumTargetSpeed);
        QueryTactical(OpponentPoints(actor.Id.Team), actor.Transform.Position, (float)Math.Min(float.MaxValue, radius),
            targetCandidates, Config.Tactics.LocalTargetCandidates);
        var indexed = actor.Id.Team is 0 or 1;
        var enemies = indexed ? EnemyTargetIndexes(actor.Id.Team) : default;
        var count = indexed ? enemies.Length : AiRows.Length;
        if (count > 0)
        {
            var offset = ((long)actor.Id.Serial * 17 + Tick / Config.Tactics.FormationReviewTicks) % count;
            var start = (int)(offset < 0 ? offset + count : offset);
            for (var n = 0; n < Math.Min(count, Config.Tactics.GlobalTargetProbes); n++)
            {
                var index = (start + n) % count; var row = indexed ? enemies[index] : index;
                if (!targetCandidates.Contains(row) && TargetAvailable(actor, AiRows[row].Entity)) targetCandidates.Add(row);
            }
        }
        targetCandidates.Sort();
    }

    private void FillFormationTargets(int leader)
    {
        FillTargetCandidates(leader); formationTargets.Clear();
        ref readonly var actor = ref AiRows[leader];
        Span<float> arrivals = stackalloc float[16];
        if (TargetAvailable(actor, actor.Brain.Target)) formationTargets.Add(aiIndexes[actor.Brain.Target]);
        var retained = formationTargets.Count;
        foreach (var target in targetCandidates)
        {
            if (!TargetAvailable(actor, AiRows[target].Entity) || formationTargets.Contains(target)) continue;
            var arrival = CachedEngageSeconds(leader, target);
            if (!float.IsFinite(arrival)) continue;
            var slot = retained;
            while (slot < formationTargets.Count && arrivals[slot] <= arrival) slot++;
            if (slot >= Config.Tactics.FormationTargetCandidates) continue;
            if (formationTargets.Count < Config.Tactics.FormationTargetCandidates) formationTargets.Add(target);
            for (var i = formationTargets.Count - 1; i > slot; i--)
            { formationTargets[i] = formationTargets[i - 1]; arrivals[i] = arrivals[i - 1]; }
            formationTargets[slot] = target; arrivals[slot] = arrival;
        }
    }

    private float EvaluateCost(int actor, Squad? squad, int target)
    {
        if (scoreWork <= 0) { Tactics.BudgetFallbacks++; return float.PositiveInfinity; }
        scoreWork--; Tactics.ScoreEvaluations++;
        return TargetCost(actor, squad, target);
    }

    private void PrepareCommittedFire()
    {
        var count = Math.Min(projectileCount, Config.Tactics.MaximumIntentProjectiles);
        if (projectileCount > count)
        {
            Collect(); SortMovingRows();
            for (var i = 0; i < count; i++) CommitIntent(projectiles[i].Entity);
        }
        else { var intentQuery = new IntentQuery { Battle = this }; RunIntentQuery(ref intentQuery); }
        // Commitments accumulate in serial order, so the rows may come in any order.
        SortBySerial(intents, static intent => intent.Serial);
        foreach (var intent in intents)
        {
            ref readonly var target = ref AiRows[intent.Target];
            committedShield[intent.Target] = MathF.Min(target.Shield.Current * 0.5f, committedShield[intent.Target] + intent.Shield);
            committedHull[intent.Target] = MathF.Min(target.Hull.Current * 0.5f, committedHull[intent.Target] + intent.Hull);
        }
        intents.Clear();
        if (projectileCount > count) Tactics.BudgetFallbacks++;
    }

    private struct IntentQuery : NativeWorld.IQuery.Read<Wrapper<Projectile>, Wrapper<Transform>, Wrapper<Motion>>
    {
        public Battle Battle;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Invoke(NativeWorld.Entity native, in Wrapper<Projectile> c0, in Wrapper<Transform> c1, in Wrapper<Motion> c2)
            => Battle.CommitIntent(c0.Value, c1.Value.Position, c2.Value.Velocity);
    }

    private void CommitIntent(EntityKey entity)
    {
        if (!Store.Alive(entity)) return;
        CommitIntent(Store.Get<Projectile>(entity), Store.Get<Transform>(entity).Position, Store.Get<Motion>(entity).Velocity);
    }

    private void CommitIntent(in Projectile shot, Vector2 position, Vector2 velocity)
    {
        Tactics.IntentProjectiles++;
        if (shot.ExpiresTick <= Tick || !aiIndexes.TryGetValue(shot.Target, out var targetIndex) ||
            !Store.Alive(shot.Owner) || !Store.Has<Identity>(shot.Owner)) return;
        ref readonly var target = ref AiRows[targetIndex];
        if (target.Docked || Store.Get<Identity>(shot.Owner).Team == target.Id.Team) return;
        var delta = target.Transform.Position - position; var distance = delta.Length(); var speed = velocity.Length();
        if (speed <= 0 || !float.IsFinite(speed + distance) || Vector2.Dot(velocity, delta) < speed * distance * 0.5f) return;
        var flight = distance / speed;
        if (flight > Config.Tactics.MaximumEngageSeconds || flight > (shot.ExpiresTick - (double)Tick) / Config.TickRate) return;
        var probability = Config.Tactics.IncomingHitConfidence *
            target.Radius / MathF.Max(target.Radius, target.Radius + target.Motion.Velocity.Length() * flight * 0.025f);
        var profile = DamageRules.ForWeapon(shot.Kind, Config.Damage);
        var damage = DamageRules.Resolve(shot.Damage, target.Shield.Current, target.Hull.Armor, profile);
        intents.Add(new(shot.Serial, targetIndex, damage.Shield * probability, damage.Hull * probability));
    }

    private float GroupWorkingRange(EntityKey entity, EntityKey target, float fallback)
    {
        if (!aiIndexes.TryGetValue(entity, out var actor) || !aiIndexes.TryGetValue(target, out var victim)) return fallback;
        if (!rangeReady[actor] || rangeTargets[actor] != target)
        {
            preferredRanges[actor] = WorkingRange(AiRows[actor], AiRows[victim]);
            rangeTargets[actor] = target; rangeReady[actor] = true;
        }
        return preferredRanges[actor] > 0 ? preferredRanges[actor] : fallback;
    }
}
