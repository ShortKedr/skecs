using SpaceBattle;
using SkecsBattle = SpaceBattle.Simulations.Skecs.Battle;

namespace BenchmarkTools.Tests;

public sealed class NativeCoreReplayTests
{
    private const int CorpusTicks = 3000;
    private static BattleConfig Config => new() { ShipsPerTeam = 8, CarriersPerTeam = 2, ChurnMultiplier = 8, ParticleMultiplier = 1 };

    // A finite regression corpus, not a proof for arbitrary configurations or platforms.
    // Detailed mechanics/invariant tests belong to SKECS. Other frameworks are observed
    // only through their externally hashed replay, with SKECS as the tick-by-tick oracle.
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ConcreteCoresMatchSkecsAtEveryTickInFiniteReplayCorpus(bool tactical, bool contactCache)
    {
        var config = Config with
        {
            Seed = contactCache ? 27u : 7331u,
            ShipsPerTeam = tactical ? 16 : 24,
            ArenaHalfSize = tactical ? 110 : 100,
            Deployment = tactical ? FleetDeployment.Tactical : FleetDeployment.LegacyRandom,
            FleetComposition = tactical ? FleetComposition.LightSwarm : FleetComposition.Uniform,
            EndlessReinforcements = tactical,
            ReplenishCarriers = !tactical,
            ReinforcementThresholdFraction = .5f,
            ReinforcementCheckIntervalTicks = 12,
            ReinforcementWarmupTicks = 12,
            ReinforcementSpawnIntervalTicks = 2,
            ReinforcementCooldownTicks = 60,
            Physics = new PhysicsSettings { CacheContactCandidates = contactCache }
        };
        using var skecs = new SkecsBattle(config);
        using var leoecslite = new SpaceBattle.Simulations.LeoEcsLite.Battle(config);
        using var leoecsclassic = new SpaceBattle.Simulations.LeoEcsClassic.Battle(config);
        using var morpeh = new SpaceBattle.Simulations.Morpeh.Battle(config);
        using var staticecs = new SpaceBattle.Simulations.StaticEcs.Battle(config);
        var skecsHash = new ReplayHash(skecs.CopyReplayState, skecs.Counters, skecs.Tactics);
        var leoecsliteHash = new ReplayHash(leoecslite.CopyReplayState, leoecslite.Counters, leoecslite.Tactics);
        var leoecsclassicHash = new ReplayHash(leoecsclassic.CopyReplayState, leoecsclassic.Counters, leoecsclassic.Tactics);
        var morpehHash = new ReplayHash(morpeh.CopyReplayState, morpeh.Counters, morpeh.Tactics);
        var staticecsHash = new ReplayHash(staticecs.CopyReplayState, staticecs.Counters, staticecs.Tactics);
        var previousContacts = new HashSet<ulong>();
        var sawContact = false;
        var sawContactEnd = false;
        var sawProjectiles = false;
        var sawParticles = false;
        for (var tick = 0; tick <= CorpusTicks; tick++)
        {
            var expected = skecsHash.Capture();
            ReplayHash.RequireMatch("LeoEcsLite", tick, expected, leoecsliteHash.Capture());
            ReplayHash.RequireMatch("LeoEcsClassic", tick, expected, leoecsclassicHash.Capture());
            ReplayHash.RequireMatch("Morpeh", tick, expected, morpehHash.Capture());
            ReplayHash.RequireMatch("StaticEcs", tick, expected, staticecsHash.Capture());
            // These SKECS-only assertions establish what the parity corpus exercises.
            var contacts = ContactPairs(skecs);
            sawContact |= contacts.Count > 0;
            sawContactEnd |= previousContacts.Any(pair => !contacts.Contains(pair));
            previousContacts.Clear();
            previousContacts.UnionWith(contacts);
            sawProjectiles |= skecs.ProjectileCount > 0;
            sawParticles |= skecs.ParticleCount > 0;
            if (tick == CorpusTicks) break;
            skecs.Step();
            leoecslite.Step();
            leoecsclassic.Step();
            morpeh.Step();
            staticecs.Step();
        }
        Assert.True(sawProjectiles && skecs.Counters.Shots > 0, "The corpus must include live projectiles.");
        Assert.True(sawParticles, "The corpus must include transient effect entities.");
        Assert.True(skecs.Counters.Builds > 0 && skecs.Counters.ShipsDestroyed > 0, "The corpus must include ship creation and retirement.");
        Assert.True(skecs.Counters.StructuralAdds > 0 && skecs.Counters.StructuralRemoves > 0, "The corpus must include component lifetime churn.");
        Assert.True(sawContact && sawContactEnd, "The corpus must include contacts starting and ending.");
        Assert.True(skecs.Tactics.FormationReviews > 0, "The corpus must include tactical decisions.");
        if (tactical)
            Assert.True(skecs.Counters.ReinforcementWaves > 0 && skecs.Counters.ReinforcementShips > 0,
                "The tactical corpus must include warp waves and actual arrivals.");
    }

    [Fact]
    public void ReplayMismatchIdentifiesFrameworkTickExpectedAndActualHash()
    {
        ReplayHash.RequireMatch("same", 0, 0x1234UL, 0x1234UL);
        var error = Assert.Throws<InvalidOperationException>(() => ReplayHash.RequireMatch("different", 3000, 0x1234UL, 0x5678UL));
        Assert.Contains("different", error.Message);
        Assert.Contains("tick 3000", error.Message);
        Assert.Contains("0000000000001234", error.Message);
        Assert.Contains("0000000000005678", error.Message);
    }

    private static HashSet<ulong> ContactPairs(SkecsBattle battle)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var physics = typeof(SkecsBattle).GetField("physics", flags)!.GetValue(battle)!;
        return (HashSet<ulong>)physics.GetType().GetField("currentPairs", flags)!.GetValue(physics)!;
    }

    [Fact]
    public void SkecsHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SkecsBattle(Config);
        Assert.True(battle.TryFindShip(1, out var ship));
        var hash = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        AssertMutation(hash, () => battle.Store.Get<Heat>(ship).Value += 1);
        AssertMutation(hash, () => battle.Store.Set(ship, new Overheated { SinceTick = 1 }));
        AssertMutation(hash, () => battle.Counters.StructuralAdds += 1L << 40);
        AssertMutation(hash, () => battle.Tactics.TargetSwitches += 1L << 40);
    }

    [Fact]
    public void SkecsRecycledNativeIdentityCannotResurrectStaleHandle()
    {
        using var battle = new SkecsBattle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void SkecsHashIncludesPersistentContactCache()
    {
        using var battle = new SkecsBattle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    [Fact]
    public void SkecsSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SkecsBattle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        var ships = keys.Where(e => battle.Has<Identity>(e)).Take(2).ToArray();
        var ship = ships[0];
        var gun = keys.First(e => battle.Has<MountedTurret>(e) && battle.Get<MountedTurret>(e).Parent == ship);
        var hull = battle.Get<Hull>(ship);
        var inventory = battle.Get<Inventory>(ship);
        var mount = battle.Get<MountedTurret>(gun);
        AssertRejectedThenRestored(battle.Validate,
            () => battle.Get<Hull>(ship).Current = -1,
            () => battle.Set(ship, hull), "Invalid ship state");
        AssertRejectedThenRestored(battle.Validate,
            () => battle.Get<Inventory>(ship).Items[0].Count = -1,
            () => battle.Set(ship, inventory), "Negative inventory");
        AssertRejectedThenRestored(battle.Validate,
            () => battle.Get<MountedTurret>(gun).Parent = EntityKey.None,
            () => battle.Set(gun, mount), "Invalid turret attachment");
        // A valid live parent and slot cannot conceal the original ship's missing mount.
        AssertRejectedThenRestored(battle.Validate,
            () => battle.Get<MountedTurret>(gun).Parent = ships[1],
            () => battle.Set(gun, mount), "Missing turret attachment");
    }

    [Fact]
    public void SkecsComprehensiveMechanicsAndInvariantsPass()
        => global::Validation.Run();

    private static void AssertContactCacheMutation(SkecsBattle battle, ReplayHash hash)
    {
        var pairs = ContactPairs(battle);
        Assert.NotEmpty(pairs);
        var before = hash.Capture();
        var contacts = pairs.ToArray();
        pairs.Remove(contacts[0]);
        Assert.NotEqual(before, hash.Capture());
        pairs.Clear();
        // Hash logical membership, independently of insertion/enumeration order.
        foreach (var pair in contacts.Reverse()) pairs.Add(pair);
        Assert.Equal(before, hash.Capture());
    }

    private static void AssertRejectedThenRestored(Action validate, Action mutate, Action restore, string reason)
    {
        validate();
        mutate();
        var error = Assert.Throws<InvalidOperationException>(validate);
        Assert.Contains(reason, error.Message);
        restore();
        validate();
    }

    private static void AssertMutation(ReplayHash hash, Action mutate)
    {
        var before = hash.Capture();
        mutate();
        var after = hash.Capture();
        Assert.NotEqual(before, after);
        var error = Assert.Throws<InvalidOperationException>(() => ReplayHash.RequireMatch("mutated", 7, before, after));
        Assert.Contains("tick 7", error.Message);
    }

    private static void AssertLifetime(Func<EntityKey> create, Action<EntityKey> destroy,
        Func<EntityKey, bool> alive, Action commit, Action<EntityKey> set, Func<EntityKey, int> read)
    {
        var retired = new List<EntityKey>();
        var recycled = false;
        for (var round = 0; round < 16; round++)
        {
            var current = create();
            set(current); commit();
            Assert.True(alive(current));
            Assert.Equal(123, read(current));
            foreach (var old in retired)
            {
                Assert.False(alive(old));
                if (old.Id == current.Id) { recycled = true; Assert.NotEqual(old.Generation, current.Generation); }
            }
            destroy(current); commit();
            Assert.False(alive(current));
            retired.Add(current);
        }
        Assert.True(recycled, "The test must exercise native slot reuse, including its generation check.");
    }
}
