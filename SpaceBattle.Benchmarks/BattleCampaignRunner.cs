using System.IO.Compression;
using System.Text.Json.Nodes;
using SKECS.BenchmarkTools;

namespace SpaceBattle.Benchmarks;

public sealed class BattleCampaignRunner(Workspace workspace)
{
    private const string BattleProject = "SpaceBattle.Benchmarks/SpaceBattle.Benchmarks.csproj";
    private const string DesktopProject = "SpaceBattle.Game/SpaceBattle.Game.csproj";
    private readonly CampaignContext context = new(workspace, "space-battle");
    private static JsonNode Clone(JsonNode? node) => node?.DeepClone() ?? throw new InvalidDataException("Missing campaign field.");

    public async Task RunAsync(string suite, string? output, bool noBuild, bool includeDesktop, bool validateOnly, int ticks, int warmup)
    {
        workspace.ConfigureEnvironment();
        if (output is not null) _ = workspace.ArtifactPath(output);
        VendorVerifier.Run(workspace.Root);
        bool battle = suite is "all" or "space-battle";
        bool desktop = suite == "desktop" || (suite == "all" && includeDesktop);
        if (!battle && !desktop) throw new ArgumentException("Unknown measurement suite: " + suite);
        if (ticks < 1 || warmup < 0) throw new ArgumentException("Ticks must be positive and warmup nonnegative.");
        if (battle && !validateOnly && (ticks != 108000 || warmup != 600))
        {
            string raw = output is null ? "artifacts/benchmarks/current/space-battle" : suite == "all" ? Path.Combine(output, "space-battle") : output;
            if (output is null || Path.GetRelativePath(workspace.PathFor("artifacts/benchmarks/current/space-battle"), workspace.ArtifactPath(raw)) == ".")
                throw new ArgumentException("Short SpaceBattle runs require a separate --output under artifacts/ to preserve current full measurements.");
        }
        var projects = new List<string> { BattleProject };
        if (desktop) projects.Add(DesktopProject);
        await context.BuildAsync(projects, noBuild);
        foreach (var flag in new[] { "--validate-adapters", "--validate-ports", "--validate" })
            await context.Host.RunAsync("dotnet", context.FileFor(BattleProject), "measure", flag);
        context.AssertStable();
        if (validateOnly) { Console.WriteLine("PASS: SpaceBattle benchmark contracts validated."); return; }
        if (battle) await BattleAsync(DataDirectory("space-battle"), ReportDirectory("space-battle"), ticks, warmup);
        if (desktop) await DesktopAsync(DataDirectory("desktop"), ReportDirectory("desktop"));
        context.AssertStable();
        Console.WriteLine("PASS: SpaceBattle benchmark campaign complete; raw files are local artifacts.");

        string DataDirectory(string name)
        {
            var directory = workspace.ArtifactPath(output is null ? $"artifacts/benchmarks/current/{name}" : suite == "all" ? Path.Combine(output, name) : output);
            Directory.CreateDirectory(directory);
            return directory;
        }
        string ReportDirectory(string name) => output is null
            ? workspace.PathFor(name == "desktop" ? "docs/space-battle/desktop-current" : "docs/space-battle/current")
            : Path.Combine(DataDirectory(name), "reports");
    }

    private async Task BattleAsync(string directory, string reports, int ticks, int warmup)
    {
        if (ticks < 1 || warmup < 0) throw new ArgumentException("Ticks must be positive and warmup nonnegative.");
        string[] frameworks = ["skecs", "lite", "classic", "morpeh", "static"];
        var manifest = BattleManifest(ticks, warmup);
        manifest["Format"] = 4;
        manifest["Implementation"] = "independent-native-cores-v1";
        manifest["FrameworkOrder"] = new JsonArray(frameworks.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
        manifest["Preset"] = "game"; manifest["Profile"] = "high";
        manifest["Workload"] = new JsonObject { ["Preset"] = "game", ["Seed"] = 7331, ["ShipsPerTeam"] = 96, ["GameplayChurnMultiplier"] = 2 };
        Workspace.Write(Path.Combine(directory, "manifest.json"), manifest);
        Workspace.Write(Path.Combine(directory, "hardware.json"), HardwareCapture.Create());
        JsonNode? baseline = null;
        foreach (var framework in frameworks)
        {
            string output = Path.Combine(directory, $"high-{framework}.json");
            await context.Host.RunAsync("dotnet", context.FileFor(BattleProject), "measure", "--framework", framework, "--ticks", ticks.ToString(), "--warmup", warmup.ToString(), "--preset", "game", "--seed", "7331", "--ships-per-team", "96", "--churn", "2", "--output", output);
            var result = Workspace.Read(output);
            var signature = new JsonObject();
            foreach (var key in new[] { "Checkpoints", "Checksum", "Counters", "Tactics" }) signature[key] = Clone(result[key]);
            var validation = result["Metadata"]!["ReplayValidation"]!;
            signature["ReplayValidation"] = Clone(validation);
            // Include every-tick validation evidence in the cross-framework signature.
            if (baseline is not null && !JsonNode.DeepEquals(baseline, signature)) throw new InvalidDataException("Cross-framework replay validation divergence: " + framework);
            baseline = signature;
        }
        context.AssertStable();
        CompleteBattle(directory, manifest, frameworks.Select(f => "high-" + f), includeHardware: true);
        if (ticks == 108000 && warmup == 600)
        {
            try { SpaceBattleReports.Run(workspace.Root, directory, verifyTicks: true, render: true, outputDirectory: reports); }
            catch { manifest["Completed"] = false; Workspace.Write(Path.Combine(directory, "manifest.json"), manifest); throw; }
        }
        else Console.WriteLine("Smoke run retained locally; full reports require 108000 ticks / 600 warmup.");
    }
    private JsonObject BattleManifest(int ticks, int warmup) => new()
    {
        ["Completed"] = false, ["StartedAt"] = CampaignContext.Now(), ["SourceRevision"] = context.Revision,
        ["SourceHashes"] = Workspace.HashObject(context.SourceHashes), ["BinaryHashes"] = Workspace.HashObject(context.BinaryHashes),
        ["BuildSkipped"] = context.BuildSkipped, ["SdkVersion"] = context.Sdk, ["Ticks"] = ticks, ["WarmupTicks"] = warmup,
        ["TieredCompilation"] = "0", ["CsvPolicy"] = "Full tick CSV bytes retained as gzip local artifacts; TickCsvHashes hashes decompressed bytes."
    };
    private async Task DesktopAsync(string directory, string reports)
    {
        const int ticks = 1200;
        var manifest = BattleManifest(ticks, 600);
        manifest["Implementation"] = "independent-native-cores-v1";
        manifest["Workload"] = new JsonObject { ["Preset"] = "game", ["Profile"] = "nominal", ["Seed"] = 7331, ["ShipsPerTeam"] = 96, ["GameplayChurnMultiplier"] = 1 };
        Workspace.Write(Path.Combine(directory, "manifest.json"), manifest);
        (string Name, string? Tiering, bool Desktop, bool Paced)[] cases =
        [ ("headless-warm-fast", "0", false, false), ("headless-warm-paced", "0", false, true),
          ("desktop-cold-fast-tiering0", "0", true, false), ("desktop-cold-fast-default", null, true, false), ("desktop-cold-paced-default", null, true, true) ];
        JsonNode? config = null;
        foreach (var entry in cases)
        {
            Environment.SetEnvironmentVariable("DOTNET_TieredCompilation", entry.Tiering);
            string output = Path.Combine(directory, entry.Name + ".json");
            var arguments = entry.Desktop
                ? new List<string> { context.FileFor(DesktopProject), "--profile-frames", ticks.ToString(), "--profile-warmup", "0", "--profile-output", output, "--profile-csv", Path.Combine(directory, entry.Name + ".ticks.csv"), "--seed", "7331", "--ships-per-team", "96", "--hidden" }
                : new List<string> { context.FileFor(BattleProject), "measure", "--preset", "game", "--framework", "skecs", "--ticks", ticks.ToString(), "--warmup", "600", "--seed", "7331", "--ships-per-team", "96", "--churn", "1", "--output", output };
            if (entry.Paced) arguments.Add(entry.Desktop ? "--profile-paced" : "--realtime");
            await context.Host.RunAsync("dotnet", arguments.ToArray());
            var result = Workspace.Read(output);
            var current = entry.Desktop ? result["Config"] : result["Metadata"]!["Options"];
            if (current!["ChurnMultiplier"]!.GetValue<double>() != 1 || current["Seed"]!.GetValue<int>() != 7331 || current["ShipsPerTeam"]!.GetValue<int>() != 96 || (config is not null && !JsonNode.DeepEquals(config, current)))
                throw new InvalidDataException("Matched desktop study configuration drift: " + entry.Name);
            config = current.DeepClone();
        }
        Environment.SetEnvironmentVariable("DOTNET_TieredCompilation", "0");
        manifest["BattleConfig"] = config;
        context.AssertStable();
        CompleteBattle(directory, manifest, cases.Select(c => c.Name), includeHardware: false);
        try { DesktopReports.Run(workspace.Root, directory, render: true, outputDirectory: reports); }
        catch { manifest["Completed"] = false; Workspace.Write(Path.Combine(directory, "manifest.json"), manifest); throw; }
    }
    private static void CompleteBattle(string directory, JsonObject manifest, IEnumerable<string> names, bool includeHardware)
    {
        var files = new JsonObject();
        var raw = new JsonObject();
        foreach (var name in names)
        {
            string csv = Path.Combine(directory, name + ".ticks.csv");
            raw[name + ".ticks.csv"] = Workspace.Hash(csv);
            using (var input = File.OpenRead(csv))
            using (var output = File.Create(csv + ".gz"))
            using (var compressed = new GZipStream(output, CompressionLevel.SmallestSize)) input.CopyTo(compressed);
            files[name + ".json"] = Workspace.Hash(Path.Combine(directory, name + ".json"));
            files[name + ".ticks.csv.gz"] = Workspace.Hash(csv + ".gz");
        }
        if (includeHardware) files["hardware.json"] = Workspace.Hash(Path.Combine(directory, "hardware.json"));
        manifest["TickCsvHashes"] = raw; manifest["Files"] = files;
        manifest["Completed"] = true; manifest["FinishedAt"] = CampaignContext.Now();
        Workspace.Write(Path.Combine(directory, "manifest.json"), manifest);
    }
}
