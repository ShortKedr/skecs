using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;

internal static class MeasurementProgram
{
    public static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            string[] switches = ["--validate-deployment", "--validate-squads", "--validate-legacy-tactics", "--validate-damage", "--damage-study", "--validate", "--validate-ports", "--realtime"];
            string[] values = ["--write-replay-oracle", "--config-json", "--framework", "--output", "--ticks", "--warmup", "--seconds", "--preset", "--seed", "--ships-per-team", "--churn", "--particles"];
            for (var i = 0; i < args.Length; i++)
            {
                if (switches.Contains(args[i])) continue;
                if (!values.Contains(args[i])) throw new ArgumentException("Unknown measurement option: " + args[i]);
                if (++i == args.Length) throw new ArgumentException("Missing value: " + args[i - 1]);
            }
            if (args.Contains("--write-replay-oracle")) { ReplayOracle.Write(args); return 0; }
            if (args.Contains("--validate-deployment")) { FleetDeploymentValidation.Run(); return 0; }
            if (args.Contains("--validate-squads")) { SquadCompositionValidation.Run(); TacticalScoringValidation.Run(); return 0; }
            if (args.Contains("--validate-legacy-tactics")) { Validation.LegacyTacticsChecks(); return 0; }
            if (args.Contains("--validate-damage")) { WeaponDamageValidation.Run(); AiLocalityValidation.RunDamage(); return 0; }
            if (args.Contains("--damage-study"))
            {
                var index = Array.IndexOf(args, "--output");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("--damage-study requires --output under artifacts/.");
                var workspace = new SKECS.BenchmarkTools.Workspace(SKECS.BenchmarkTools.Workspace.FindRoot());
                var output = workspace.ArtifactPath(args[index + 1]);
                if (File.Exists(output)) throw new ArgumentException("Use a fresh output file under artifacts/.");
                var study = WeaponDamageValidation.CreateControlledStudy();
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(study, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Wrote {study.Revision}: {study.Results.Length} equal-base-DPS controlled layer comparisons to {output}");
                return 0;
            }
            if (args.Contains("--validate")) { Validation.Run(); return 0; }
            if (args.Contains("--validate-ports")) { NativeReplayValidation.Run(args); return 0; }
            var options = RunOptions.Parse(args);
            var frameworkIndex = Array.IndexOf(args, "--framework");
            var framework = frameworkIndex < 0 ? "skecs" : args[frameworkIndex + 1];
            switch (framework)
            {
                case "skecs": SkecsRunner.Run(options); break;
                case "lite": LeoEcsLiteRunner.Run(options); break;
                case "classic": LeoEcsClassicRunner.Run(options); break;
                case "morpeh": MorpehRunner.Run(options); break;
                case "static": StaticEcsRunner.Run(options); break;
                default: throw new ArgumentException("--framework must be skecs, lite, classic, morpeh, or static");
            }
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}

internal sealed record RunOptions(string Output, int Ticks, int WarmupTicks, BattleConfig Config, bool Realtime, string Preset)
{
    public static RunOptions Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length) throw new ArgumentException($"Missing value: {name}");
            return args[index + 1];
        }
        var seconds = double.Parse(Value("--seconds", "1800"));
        var ticks = int.Parse(Value("--ticks", ((int)(seconds * 60)).ToString()));
        var warmup = int.Parse(Value("--warmup", "600"));
        if (ticks < 1 || warmup < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        var preset = Value("--preset", "benchmark");
        if (preset is not ("benchmark" or "game")) throw new ArgumentException("--preset must be benchmark or game");
        var game = preset == "game";
        var config = new BattleConfig
        {
            Seed = uint.Parse(Value("--seed", "7331")), ShipsPerTeam = int.Parse(Value("--ships-per-team", "96")),
            ChurnMultiplier = float.Parse(Value("--churn", game ? "2" : "1")), ParticleMultiplier = int.Parse(Value("--particles", "1")),
            FleetComposition = game ? FleetComposition.LightSwarm : FleetComposition.Uniform,
            Deployment = game ? FleetDeployment.Tactical : FleetDeployment.LegacyRandom,
            EndlessReinforcements = game, ReplenishCarriers = !game
        };
        config.Validate();
        var workspace = new SKECS.BenchmarkTools.Workspace(SKECS.BenchmarkTools.Workspace.FindRoot(null));
        var output = workspace.ArtifactPath(Value("--output", "artifacts/benchmarks/space-battle-single/skecs.json"));
        return new(output, ticks, warmup, config, args.Contains("--realtime"), preset);
    }
}

internal static class Runner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };
    public static TickStats Stats(ReadOnlySpan<double> values)
    {
        var sorted = values.ToArray(); Array.Sort(sorted); var sum = 0.0; var sumSquared = 0.0; var misses = 0;
        foreach (var v in sorted) { sum += v; sumSquared += v * v; if (v > 1000.0 / 60) misses++; }
        var mean = sum / sorted.Length;
        double Percentile(double p) => sorted[(int)Math.Ceiling((sorted.Length - 1) * p)];
        return new(mean, sorted.Length > 1 ? Math.Sqrt(Math.Max(0, (sumSquared - sum * sum / sorted.Length) / (sorted.Length - 1))) : 0,
            Percentile(0.5), Percentile(0.95), Percentile(0.99), sorted[^1], misses, misses * 100.0 / sorted.Length);
    }
    public sealed record TickStats(double MeanMs, double StddevMs, double MedianMs, double P95Ms, double P99Ms, double MaxMs, int BudgetMisses, double BudgetMissPercent);
}
