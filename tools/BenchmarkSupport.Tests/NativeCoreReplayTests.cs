using SpaceBattle;

namespace BenchmarkTools.Tests;

public sealed class NativeCoreReplayTests
{
    [Fact]
    public void StaticEcsBrowserBatchDeletionSelectsOnlyOneEntityAcrossBlockSegmentAndChunkBoundaries()
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(Config);
        var gameplay = new List<EntityKey>(); battle.CopyEntityKeys(gameplay);
        var extras = new List<EntityKey>();
        for (var i = 0; i < 4200; i++)
        {
            var e = battle.Create(); battle.Set(e, new Lifetime { EndTick = i + 10000 }); extras.Add(e);
        }
        var destroy = typeof(SpaceBattle.Simulations.StaticEcs.Battle).GetMethod("DestroyBrowserEntity",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var deleted = new HashSet<EntityKey>();
        foreach (var id in new[] { 127, 128, 255, 256, 4095, 4096 })
        {
            var e = extras.Single(e => e.Id == id);
            destroy.Invoke(null, new object[] { e });
            deleted.Add(e);
            Assert.False(battle.Alive(e));
        }
        for (var i = 0; i < extras.Count; i++)
        {
            var e = extras[i];
            Assert.Equal(!deleted.Contains(e), battle.Alive(e));
            if (!deleted.Contains(e)) Assert.Equal(i + 10000, battle.Get<Lifetime>(e).EndTick);
        }
        foreach (var e in gameplay) Assert.True(battle.Alive(e));
    }

    [Theory]
    [InlineData("", "D41D8CD98F00B204E9800998ECF8427E")]
    [InlineData("a", "0CC175B9C0F1B6A831C399E269772661")]
    [InlineData("abc", "900150983CD24FB0D6963F7D28E17F72")]
    [InlineData("message digest", "F96B697D7CB7938D525A2F31AAF161D0")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "C3FCD3D76192E4007DFB496CCA67E13B")]
    public void StaticEcsBrowserTypeGuidHashMatchesKnownMd5Vectors(string text, string expected)
        => Assert.Equal(expected, Convert.ToHexString(SKECS.BenchmarkCompatibility.StaticEcsMd5.ComputeManaged(System.Text.Encoding.UTF8.GetBytes(text))));

    [Fact]
    public void StaticEcsBrowserTypeGuidHashMatchesBclAcrossPaddingAndBlockBoundaries()
    {
        var random = new Random(7331);
        foreach (var length in new[] { 0, 1, 55, 56, 63, 64, 65, 119, 120, 127, 128, 4096 })
        {
            var bytes = new byte[length]; random.NextBytes(bytes);
            Assert.Equal(System.Security.Cryptography.MD5.HashData(bytes), SKECS.BenchmarkCompatibility.StaticEcsMd5.ComputeManaged(bytes));
            using var factory = SKECS.BenchmarkCompatibility.StaticEcsMd5.Create();
            Assert.Equal(System.Security.Cryptography.MD5.HashData(bytes), factory.ComputeHash(bytes));
        }
    }

    private static BattleConfig Config => new() { ShipsPerTeam = 8, CarriersPerTeam = 2, ChurnMultiplier = 8, ParticleMultiplier = 1 };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteCoresMatchEveryTickAcrossLiveBattleCorpus(bool tactical)
    {
        var config = Config with { Deployment = tactical ? FleetDeployment.Tactical : FleetDeployment.LegacyRandom, FleetComposition = tactical ? FleetComposition.LightSwarm : FleetComposition.Uniform, EndlessReinforcements = tactical };
        using var skecs = new SpaceBattle.Simulations.Skecs.Battle(config);
        using var leoecslite = new SpaceBattle.Simulations.LeoEcsLite.Battle(config);
        using var leoecsclassic = new SpaceBattle.Simulations.LeoEcsClassic.Battle(config);
        using var morpeh = new SpaceBattle.Simulations.Morpeh.Battle(config);
        using var staticecs = new SpaceBattle.Simulations.StaticEcs.Battle(config);
        var skecsHash = new ReplayHash(skecs.CopyReplayState, skecs.Counters, skecs.Tactics);
        var leoecsliteHash = new ReplayHash(leoecslite.CopyReplayState, leoecslite.Counters, leoecslite.Tactics);
        var leoecsclassicHash = new ReplayHash(leoecsclassic.CopyReplayState, leoecsclassic.Counters, leoecsclassic.Tactics);
        var morpehHash = new ReplayHash(morpeh.CopyReplayState, morpeh.Counters, morpeh.Tactics);
        var staticecsHash = new ReplayHash(staticecs.CopyReplayState, staticecs.Counters, staticecs.Tactics);
        for (var tick = 0; tick <= 480; tick++)
        {
            var expected = skecsHash.Capture();
            ReplayHash.RequireMatch("LeoEcsLite", tick, expected, leoecsliteHash.Capture());
            ReplayHash.RequireMatch("LeoEcsClassic", tick, expected, leoecsclassicHash.Capture());
            ReplayHash.RequireMatch("Morpeh", tick, expected, morpehHash.Capture());
            ReplayHash.RequireMatch("StaticEcs", tick, expected, staticecsHash.Capture());
            if (tick == 480) break;
            skecs.Step();
            leoecslite.Step();
            leoecsclassic.Step();
            morpeh.Step();
            staticecs.Step();
        }
        Assert.True(skecs.Counters.Shots > 0, "The corpus must include weapons and moving entities.");
        Assert.True(skecs.Counters.Builds > 0, "The corpus must include structural creation.");
        Assert.True(skecs.Tactics.FormationReviews > 0, "The corpus must include tactical decisions.");
    }

    [Fact]
    public void SkecsHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(Config);
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
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void LeoEcsLiteHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsLite.Battle(Config);
        Assert.True(battle.TryFindShip(1, out var ship));
        var hash = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        AssertMutation(hash, () => battle.Store.Get<Heat>(ship).Value += 1);
        AssertMutation(hash, () => battle.Store.Set(ship, new Overheated { SinceTick = 1 }));
        AssertMutation(hash, () => battle.Counters.StructuralAdds += 1L << 40);
        AssertMutation(hash, () => battle.Tactics.TargetSwitches += 1L << 40);
    }

    [Fact]
    public void LeoEcsLiteRecycledNativeIdentityCannotResurrectStaleHandle()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsLite.Battle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void LeoEcsClassicHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsClassic.Battle(Config);
        Assert.True(battle.TryFindShip(1, out var ship));
        var hash = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        AssertMutation(hash, () => battle.Store.Get<Heat>(ship).Value += 1);
        AssertMutation(hash, () => battle.Store.Set(ship, new Overheated { SinceTick = 1 }));
        AssertMutation(hash, () => battle.Counters.StructuralAdds += 1L << 40);
        AssertMutation(hash, () => battle.Tactics.TargetSwitches += 1L << 40);
    }

    [Fact]
    public void LeoEcsClassicRecycledNativeIdentityCannotResurrectStaleHandle()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsClassic.Battle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void MorpehHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SpaceBattle.Simulations.Morpeh.Battle(Config);
        Assert.True(battle.TryFindShip(1, out var ship));
        var hash = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        AssertMutation(hash, () => battle.Store.Get<Heat>(ship).Value += 1);
        AssertMutation(hash, () => battle.Store.Set(ship, new Overheated { SinceTick = 1 }));
        AssertMutation(hash, () => battle.Counters.StructuralAdds += 1L << 40);
        AssertMutation(hash, () => battle.Tactics.TargetSwitches += 1L << 40);
    }

    [Fact]
    public void MorpehRecycledNativeIdentityCannotResurrectStaleHandle()
    {
        using var battle = new SpaceBattle.Simulations.Morpeh.Battle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void StaticEcsHashDetectsComponentTagCounterAndTacticalMutations()
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(Config);
        Assert.True(battle.TryFindShip(1, out var ship));
        var hash = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        AssertMutation(hash, () => battle.Store.Get<Heat>(ship).Value += 1);
        AssertMutation(hash, () => battle.Store.Set(ship, new Overheated { SinceTick = 1 }));
        AssertMutation(hash, () => battle.Counters.StructuralAdds += 1L << 40);
        AssertMutation(hash, () => battle.Tactics.TargetSwitches += 1L << 40);
    }

    [Fact]
    public void StaticEcsRecycledNativeIdentityCannotResurrectStaleHandle()
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(Config);
        AssertLifetime(battle.Create, battle.Destroy, battle.Alive, battle.Commit,
            e => battle.Set(e, new Lifetime { EndTick = 123 }),
            e => battle.Get<Lifetime>(e).EndTick);
    }

    [Fact]
    public void SkecsHashIncludesPersistentContactCache()
    {
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    [Fact]
    public void LeoEcsLiteHashIncludesPersistentContactCache()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsLite.Battle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    [Fact]
    public void LeoEcsClassicHashIncludesPersistentContactCache()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsClassic.Battle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    [Fact]
    public void MorpehHashIncludesPersistentContactCache()
    {
        using var battle = new SpaceBattle.Simulations.Morpeh.Battle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    [Fact]
    public void StaticEcsHashIncludesPersistentContactCache()
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(Config);
        var keys = new List<EntityKey>(); battle.CopyEntityKeys(keys);
        // The first rows are ships. Force a real overlap to create contact history.
        battle.Get<Transform>(keys[1]) = battle.Get<Transform>(keys[0]);
        battle.Step();
        AssertContactCacheMutation(battle, new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics));
    }

    private static void AssertContactCacheMutation(object battle, ReplayHash hash)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var physics = battle.GetType().GetField("physics", flags)!.GetValue(battle)!;
        var pairs = (HashSet<ulong>)physics.GetType().GetField("currentPairs", flags)!.GetValue(physics)!;
        Assert.NotEmpty(pairs);
        var before = hash.Capture();
        var contacts = pairs.ToArray();
        pairs.Remove(contacts[0]);
        Assert.NotEqual(before, hash.Capture());
        pairs.Clear();
        // Hashing observes the logical set, independent of insertion/enumeration order.
        foreach (var pair in contacts.Reverse()) pairs.Add(pair);
        Assert.Equal(before, hash.Capture());
    }
    [Fact]
    public void SkecsSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(Config);
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
    public void LeoEcsLiteSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsLite.Battle(Config);
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
    public void LeoEcsClassicSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsClassic.Battle(Config);
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
    public void MorpehSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SpaceBattle.Simulations.Morpeh.Battle(Config);
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
    public void StaticEcsSoftwareValidationRejectsInvalidComponentsAndMountRosters()
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(Config);
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
