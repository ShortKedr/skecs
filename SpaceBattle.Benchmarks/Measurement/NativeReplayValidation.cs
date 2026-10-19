using SpaceBattle;

internal static class NativeReplayValidation
{
    // Generate the SKECS oracle before warmup/timing. It never runs inside measured Step.
    public static ulong[] Reference(BattleConfig config, int ticks)
    {
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        var hashes = new ulong[ticks + 1];
        hashes[0] = replay.Capture();
        for (var tick = 1; tick <= ticks; tick++) { battle.Step(); hashes[tick] = replay.Capture(); }
        battle.Validate();
        return hashes;
    }

    public static void Run(string[] args)
    {
        var defaults = args.ToList();
        void Default(string key, string value) { if (!defaults.Contains(key)) { defaults.Add(key); defaults.Add(value); } }
        Default("--ticks", "3000"); Default("--ships-per-team", "16"); Default("--seed", "27");
        var options = RunOptions.Parse(defaults.ToArray());
        var configs = args.Contains("--preset") ? new[] { options.Config } : new[]
        {
            options.Config,
            options.Config with { FleetComposition = FleetComposition.LightSwarm, Deployment = FleetDeployment.Tactical,
                EndlessReinforcements = true, ReplenishCarriers = false, ChurnMultiplier = 2 }
        };
        foreach (var config in configs)
        {
            var expected = Reference(config, options.Ticks);
            CompareSkecs(config, expected);
            CompareLeoEcsLite(config, expected);
            CompareLeoEcsClassic(config, expected);
            CompareMorpeh(config, expected);
            CompareStaticEcs(config, expected);
        }
        Console.WriteLine("PASS: all five independent native cores match SKECS at every tick, including initial state.");
    }

    private static void CompareSkecs(BattleConfig config, ulong[] expected)
    {
        using var battle = new SpaceBattle.Simulations.Skecs.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        ReplayHash.RequireMatch(battle.Framework, 0, expected[0], replay.Capture());
        for (var tick = 1; tick < expected.Length; tick++)
        {
            battle.Step();
            ReplayHash.RequireMatch(battle.Framework, tick, expected[tick], replay.Capture());
        }
        battle.Validate();
        Console.WriteLine($"PASS: {battle.Framework}, {expected.Length - 1} ticks, deployment={config.Deployment}, hash={expected[^1]:X16}");
    }

    private static void CompareLeoEcsLite(BattleConfig config, ulong[] expected)
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsLite.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        ReplayHash.RequireMatch(battle.Framework, 0, expected[0], replay.Capture());
        for (var tick = 1; tick < expected.Length; tick++)
        {
            battle.Step();
            ReplayHash.RequireMatch(battle.Framework, tick, expected[tick], replay.Capture());
        }
        battle.Validate();
        Console.WriteLine($"PASS: {battle.Framework}, {expected.Length - 1} ticks, deployment={config.Deployment}, hash={expected[^1]:X16}");
    }

    private static void CompareLeoEcsClassic(BattleConfig config, ulong[] expected)
    {
        using var battle = new SpaceBattle.Simulations.LeoEcsClassic.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        ReplayHash.RequireMatch(battle.Framework, 0, expected[0], replay.Capture());
        for (var tick = 1; tick < expected.Length; tick++)
        {
            battle.Step();
            ReplayHash.RequireMatch(battle.Framework, tick, expected[tick], replay.Capture());
        }
        battle.Validate();
        Console.WriteLine($"PASS: {battle.Framework}, {expected.Length - 1} ticks, deployment={config.Deployment}, hash={expected[^1]:X16}");
    }

    private static void CompareMorpeh(BattleConfig config, ulong[] expected)
    {
        using var battle = new SpaceBattle.Simulations.Morpeh.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        ReplayHash.RequireMatch(battle.Framework, 0, expected[0], replay.Capture());
        for (var tick = 1; tick < expected.Length; tick++)
        {
            battle.Step();
            ReplayHash.RequireMatch(battle.Framework, tick, expected[tick], replay.Capture());
        }
        battle.Validate();
        Console.WriteLine($"PASS: {battle.Framework}, {expected.Length - 1} ticks, deployment={config.Deployment}, hash={expected[^1]:X16}");
    }

    private static void CompareStaticEcs(BattleConfig config, ulong[] expected)
    {
        using var battle = new SpaceBattle.Simulations.StaticEcs.Battle(config);
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        ReplayHash.RequireMatch(battle.Framework, 0, expected[0], replay.Capture());
        for (var tick = 1; tick < expected.Length; tick++)
        {
            battle.Step();
            ReplayHash.RequireMatch(battle.Framework, tick, expected[tick], replay.Capture());
        }
        battle.Validate();
        Console.WriteLine($"PASS: {battle.Framework}, {expected.Length - 1} ticks, deployment={config.Deployment}, hash={expected[^1]:X16}");
    }
}
