using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace SKECS.BenchmarkTools;

public sealed class CampaignRunner(Workspace workspace)
{
    private const string ComparisonProject = "Benchmarks.Local/Comparison.csproj";
    private const string CurrentProject = "SKECS.Benchmarks/SKECS.Benchmarks.csproj";
    private const string LegacyProject = "SKECS.OtherBenchmarks/SKECS.OtherBenchmarks.csproj";
    private const string BattleProject = "SpaceBattle.Headless/SpaceBattle.Headless.csproj";
    private const string DesktopProject = "SpaceBattle.Game/SpaceBattle.Game.csproj";
    private readonly ProcessHost host = new(workspace.Root);
    private SortedDictionary<string, string> sources = new(StringComparer.Ordinal);
    private SortedDictionary<string, string> binaries = new(StringComparer.Ordinal);
    private string revision = "source-tree";
    private string sdk = "";
    private bool skippedBuild;
    private bool dirty;
    private static JsonNode Clone(JsonNode? node) => node?.DeepClone() ?? throw new InvalidDataException("Missing campaign field.");
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private static string Dll(string project) => Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net8.0", Path.GetFileNameWithoutExtension(project) + ".dll");
    private string FileFor(string project) => workspace.PathFor(Dll(project));

    public async Task RunAsync(string suite, string? output, bool noBuild, bool includeDesktop, bool validateOnly, int ticks, int warmup)
    {
        workspace.ConfigureEnvironment();
        if (output is not null) _ = workspace.ArtifactPath(output);
        if (BenchmarkCodeGenerator.Run(workspace.Root, check: true) != 0) throw new InvalidDataException("Generated benchmark sources are stale; run generate.");
        VendorVerifier.Run(workspace.Root);
        bool comparison = suite is "all" or "comparison";
        bool supplement = suite is "all" or "supplement";
        bool battle = suite is "all" or "space-battle";
        bool desktop = suite == "desktop" || (suite == "all" && includeDesktop);
        if (!comparison && !supplement && !battle && !desktop) throw new ArgumentException("Unknown benchmark suite: " + suite);
        if (battle && (ticks < 1 || warmup < 0)) throw new ArgumentException("Ticks must be positive and warmup nonnegative.");
        if (battle && !validateOnly && (ticks != 108000 || warmup != 600))
        {
            string raw = output is null ? "artifacts/benchmarks/current/space-battle" : suite == "all" ? Path.Combine(output, "space-battle") : output;
            if (output is null || Path.GetRelativePath(workspace.PathFor("artifacts/benchmarks/current/space-battle"), workspace.ArtifactPath(raw)) == ".")
                throw new ArgumentException("Short SpaceBattle runs require a separate --output under artifacts/ to preserve current full measurements.");
        }
        var projects = new List<string>();
        if (comparison || supplement) projects.Add(ComparisonProject);
        if (supplement) projects.AddRange([CurrentProject, LegacyProject]);
        if (battle || desktop) projects.Add(BattleProject);
        if (desktop) projects.Add(DesktopProject);
        await BuildAsync(projects, noBuild);
        revision = await host.CaptureAsync("git", ["rev-parse", "HEAD"], allowFailure: true);
        if (revision.Length == 0) revision = "source-tree";
        dirty = revision == "source-tree" || (await host.CaptureAsync("git", ["status", "--porcelain", "--untracked-files=normal"], allowFailure: true)).Length > 0;
        Environment.SetEnvironmentVariable("SKECS_BENCHMARK_REVISION", revision);
        Environment.SetEnvironmentVariable("SKECS_STRESS_EXTREME", "0");
        Environment.SetEnvironmentVariable("SKECS_BENCHMARK_SCENARIOS", null);
        sdk = await host.CaptureAsync("dotnet", ["--version"]);
        if (comparison)
        {
            await host.RunAsync("dotnet", FileFor(ComparisonProject), "--validate");
            await host.RunAsync("dotnet", FileFor(ComparisonProject), "--validate-stress");
        }
        if (supplement) await host.RunAsync("dotnet", FileFor(LegacyProject), "--validate");
        if (battle || desktop)
            foreach (var flag in new[] { "--validate-adapters", "--validate-ports", "--validate" })
                await host.RunAsync("dotnet", FileFor(BattleProject), flag);
        AssertStable();
        if (validateOnly) { Console.WriteLine("PASS: requested benchmark contracts validated."); return; }
        if (comparison) await ComparisonAsync(DataDirectory("micro"), ReportDirectory("micro"));
        if (battle) await BattleAsync(DataDirectory("space-battle"), ReportDirectory("space-battle"), ticks, warmup);
        if (desktop) await DesktopAsync(DataDirectory("desktop"), ReportDirectory("desktop"));
        if (supplement) await SupplementAsync(DataDirectory("micro"), ReportDirectory("micro"));
        AssertStable();
        Console.WriteLine("PASS: benchmark campaign complete; raw files are local artifacts.");

        string DataDirectory(string name)
        {
            var directory = workspace.ArtifactPath(output is null ? $"artifacts/benchmarks/current/{name}" : (suite == "all" ? Path.Combine(output, name) : output));
            Directory.CreateDirectory(directory);
            return directory;
        }
        string ReportDirectory(string name) => output is null
            ? workspace.PathFor(name == "micro" ? "docs/benchmarks/current" : name == "desktop" ? "docs/space-battle/desktop-current" : "docs/space-battle/current")
            : Path.Combine(DataDirectory(name), "reports");
    }

    private async Task BuildAsync(List<string> projects, bool noBuild)
    {
        sources = workspace.Sources();
        skippedBuild = noBuild;
        string snapshot = workspace.PathFor("Benchmarks.Local/tmp/dotnet-benchmark-build.json");
        if (noBuild)
        {
            if (!File.Exists(snapshot)) throw new InvalidDataException("No certified .NET benchmark build. Run without --no-build first.");
            var build = Workspace.Read(snapshot);
            if (!JsonNode.DeepEquals(build["SourceHashes"], Workspace.HashObject(sources))) throw new InvalidDataException("Sources differ from the certified build; run without --no-build.");
            var recorded = build["Projects"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            if (projects.Any(p => !recorded.Contains(p))) throw new InvalidDataException("The certified build does not include this suite.");
            foreach (var pair in build["AssemblyHashes"]!.AsObject())
                if (!File.Exists(workspace.PathFor(pair.Key)) || Workspace.Hash(workspace.PathFor(pair.Key)) != pair.Value!.GetValue<string>())
                    throw new InvalidDataException("Certified binary changed: " + pair.Key);
        }
        else
        {
            foreach (var project in projects)
            {
                await host.RunAsync("dotnet", "restore", project, "--ignore-failed-sources", "--disable-parallel", "-p:NuGetAudit=false");
                await host.RunAsync("dotnet", "build", project, "-c", "Release", "--no-restore", "-m:1", "-nr:false", "-p:GeneratePackageOnBuild=false", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false");
            }
        }
        workspace.AssertHashes(sources);
        if (!JsonNode.DeepEquals(Workspace.HashObject(sources), Workspace.HashObject(workspace.Sources()))) throw new InvalidDataException("Source inventory changed during compilation.");
        binaries = workspace.Binaries(projects);
        if (!noBuild) Workspace.Write(snapshot, new JsonObject { ["SourceHashes"] = Workspace.HashObject(sources), ["AssemblyHashes"] = Workspace.HashObject(binaries), ["Projects"] = new JsonArray(projects.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) });
    }
    private void AssertStable()
    {
        workspace.AssertHashes(sources);
        workspace.AssertHashes(binaries);
        if (!JsonNode.DeepEquals(Workspace.HashObject(sources), Workspace.HashObject(workspace.Sources()))) throw new InvalidDataException("Source inventory changed during measurement.");
    }
    private JsonObject Common(string started) => new()
    {
        ["format"] = 2, ["started"] = started, ["finished"] = Now(), ["sourceRevision"] = revision,
        ["sourceHashes"] = Workspace.HashObject(sources), ["assemblyHashes"] = Workspace.HashObject(binaries),
        ["buildSkipped"] = skippedBuild, ["sdk"] = sdk, ["cpu"] = Workspace.CpuName(), ["logicalProcessors"] = Environment.ProcessorCount
    };
    private async Task ComparisonAsync(string directory, string reports)
    {
        string started = Now();
        Workspace.Write(Path.Combine(directory, "campaign.json"), new JsonObject { ["completed"] = false, ["started"] = started });
        await host.RunAsync("dotnet", FileFor(ComparisonProject), Path.Combine(directory, "base.json"));
        await host.RunAsync("dotnet", FileFor(ComparisonProject), "--stress", Path.Combine(directory, "stress.json"));
        AssertStable();
        var manifest = Common(started);
        manifest["completed"] = true;
        manifest["workingTreeModified"] = dirty;
        manifest["gitMetadataAvailable"] = revision != "source-tree";
        manifest["measurementAssemblySha256"] = Workspace.Hash(FileFor(ComparisonProject));
        manifest["skecsAssemblySha256"] = Workspace.Hash(Path.Combine(Path.GetDirectoryName(FileFor(ComparisonProject))!, "SKECS.dll"));
        manifest["baseInputSha256"] = Workspace.Hash(Path.Combine(directory, "base.json"));
        manifest["stressInputSha256"] = Workspace.Hash(Path.Combine(directory, "stress.json"));
        manifest["matrix"] = new JsonObject { ["base"] = 40, ["stress"] = 70, ["frameworks"] = 5 };
        manifest["methodology"] = new JsonObject { ["baseSamples"] = 20, ["stressSamples"] = 8, ["seed"] = 2048, ["singleThread"] = true, ["tiering"] = "off", ["caseOrder"] = "deterministically shuffled within each suite" };
        manifest["validation"] = new JsonObject { ["base"] = "passed before timing", ["stress"] = "passed before timing and after each stress case" };
        manifest["command"] = "dotnet run --project tools/BenchmarkTools -c Release -- run comparison";
        Workspace.Write(Path.Combine(directory, "campaign.json"), manifest);
        try { MicroReports.Comparison(workspace.Root, directory, render: true, outputDirectory: reports); }
        catch { manifest["completed"] = false; Workspace.Write(Path.Combine(directory, "campaign.json"), manifest); throw; }
    }
    private async Task SupplementAsync(string directory, string reports)
    {
        string comparison = Path.Combine(directory, "campaign.json");
        if (File.Exists(comparison))
        {
            var prior = Workspace.Read(comparison);
            if (prior["sourceRevision"]?.GetValue<string>() != revision || !JsonNode.DeepEquals(prior["sourceHashes"], Workspace.HashObject(sources)))
                throw new InvalidDataException("Comparison belongs to a different source snapshot. Run comparison first, or use a fresh --output for supplement.");
            foreach (var pair in prior["assemblyHashes"]!.AsObject().Where(p => p.Key.StartsWith("Benchmarks.Local/bin/Release/net8.0/", StringComparison.Ordinal)))
                if (!binaries.TryGetValue(pair.Key, out var digest) || !string.Equals(pair.Value!.GetValue<string>(), digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Comparison binary differs; run comparison first or use a fresh --output: " + pair.Key);
        }
        string started = Now();
        Workspace.Write(Path.Combine(directory, "supplement.json"), new JsonObject { ["completed"] = false, ["started"] = started });
        await host.RunAsync("dotnet", FileFor(ComparisonProject), "--optimized-api", Path.Combine(directory, "optimized-api.json"));
        await host.RunAsync("dotnet", FileFor(ComparisonProject), "--storage", Path.Combine(directory, "storage.json"));
        foreach (var (name, project, expected) in new[] { ("skecs", CurrentProject, 34), ("legacy", LegacyProject, 51) })
        {
            string suiteStarted = Now();
            string artifacts = Path.Combine(directory, "bdn-work", name);
            string rawDirectory = Path.Combine(directory, "bdn-raw", name);
            Directory.CreateDirectory(rawDirectory);
            // Each run gets a distinct work folder, so old partial exporters cannot join this campaign.
            artifacts = Path.Combine(artifacts, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffff"));
            await host.RunAsync("dotnet", FileFor(project), "--filter", "*", "--job", "Short", "--iterationCount", "8", "--warmupCount", "3", "--inProcess", "--exporters", "fulljson", "--artifacts", artifacts);
            var rows = new JsonArray();
            var rawReports = new JsonArray();
            JsonNode? last = null;
            foreach (var file in Directory.EnumerateFiles(Path.Combine(artifacts, "results"), "*-report-full*.json").Order(StringComparer.Ordinal))
            {
                last = Workspace.Read(file);
                foreach (var row in last["Benchmarks"]!.AsArray()) rows.Add(Clone(row));
                string target = Path.Combine(rawDirectory, Path.GetFileName(file));
                File.Copy(file, target, true);
                rawReports.Add(new JsonObject { ["file"] = $"bdn-raw/{name}/{Path.GetFileName(file)}", ["sha256"] = Workspace.Hash(target) });
            }
            if (rows.Count != expected) throw new InvalidDataException($"Incomplete {name} BDN matrix: {rows.Count}/{expected}");
            AssertStable();
            var metadata = new JsonObject
            {
                ["sourceRevision"] = revision, ["sourceHashes"] = Workspace.HashObject(sources),
                ["configuration"] = "Release", ["tieredCompilation"] = "0", ["job"] = "Short", ["toolchain"] = "InProcess",
                ["warmupCount"] = 3, ["iterationCount"] = 8, ["runtime"] = Clone(last!["HostEnvironmentInfo"]!["RuntimeVersion"]),
                ["assemblySha256"] = Workspace.Hash(FileFor(project)), ["started"] = suiteStarted, ["finished"] = Now(),
                ["expectedCases"] = expected, ["buildSkipped"] = skippedBuild,
                ["note"] = "All raw iterations retained; BenchmarkDotNet statistics use its configured outlier policy. Five-million-entity stress is opt-in."
            };
            Workspace.Write(Path.Combine(directory, $"bdn-{name}.json"), new JsonObject { ["metadata"] = metadata, ["Benchmarks"] = rows, ["rawReports"] = rawReports });
        }
        AssertStable();
        var manifest = Common(started);
        manifest["completed"] = true;
        manifest["comparisonAssemblySha256"] = Workspace.Hash(FileFor(ComparisonProject));
        var inputs = new JsonObject();
        foreach (var name in new[] { "optimized-api.json", "storage.json", "bdn-skecs.json", "bdn-legacy.json" }) inputs[name] = Workspace.Hash(Path.Combine(directory, name));
        manifest["inputHashes"] = inputs;
        manifest["command"] = "dotnet run --project tools/BenchmarkTools -c Release -- run supplement";
        Workspace.Write(Path.Combine(directory, "supplement.json"), manifest);
        try { MicroReports.Supplement(workspace.Root, directory, render: true, outputDirectory: reports); }
        catch { manifest["completed"] = false; Workspace.Write(Path.Combine(directory, "supplement.json"), manifest); throw; }
    }

    private async Task BattleAsync(string directory, string reports, int ticks, int warmup)
    {
        if (ticks < 1 || warmup < 0) throw new ArgumentException("Ticks must be positive and warmup nonnegative.");
        string[] frameworks = ["skecs", "lite", "classic", "morpeh", "static"];
        var manifest = BattleManifest(ticks, warmup);
        manifest["Format"] = 3;
        manifest["FrameworkOrder"] = new JsonArray(frameworks.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
        manifest["Preset"] = "game"; manifest["Profile"] = "high";
        manifest["Workload"] = new JsonObject { ["Preset"] = "game", ["Seed"] = 7331, ["ShipsPerTeam"] = 96, ["GameplayChurnMultiplier"] = 2 };
        Workspace.Write(Path.Combine(directory, "manifest.json"), manifest);
        Workspace.Write(Path.Combine(directory, "hardware.json"), HardwareCapture.Create());
        JsonNode? baseline = null;
        foreach (var framework in frameworks)
        {
            string output = Path.Combine(directory, $"high-{framework}.json");
            await host.RunAsync("dotnet", FileFor(BattleProject), "--framework", framework, "--ticks", ticks.ToString(), "--warmup", warmup.ToString(), "--preset", "game", "--seed", "7331", "--ships-per-team", "96", "--churn", "2", "--output", output);
            var result = Workspace.Read(output);
            var signature = new JsonObject();
            foreach (var key in new[] { "Checkpoints", "Checksum", "Counters", "Tactics" }) signature[key] = Clone(result[key]);
            if (baseline is not null && !JsonNode.DeepEquals(baseline, signature)) throw new InvalidDataException("Cross-framework gameplay divergence: " + framework);
            baseline = signature;
        }
        AssertStable();
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
        ["Completed"] = false, ["StartedAt"] = Now(), ["SourceRevision"] = revision,
        ["SourceHashes"] = Workspace.HashObject(sources), ["BinaryHashes"] = Workspace.HashObject(binaries),
        ["BuildSkipped"] = skippedBuild, ["SdkVersion"] = sdk, ["Ticks"] = ticks, ["WarmupTicks"] = warmup,
        ["TieredCompilation"] = "0", ["CsvPolicy"] = "Full tick CSV bytes retained as gzip local artifacts; TickCsvHashes hashes decompressed bytes."
    };
    private async Task DesktopAsync(string directory, string reports)
    {
        const int ticks = 1200;
        var manifest = BattleManifest(ticks, 600);
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
                ? new List<string> { FileFor(DesktopProject), "--profile-frames", ticks.ToString(), "--profile-warmup", "0", "--profile-output", output, "--profile-csv", Path.Combine(directory, entry.Name + ".ticks.csv"), "--seed", "7331", "--ships-per-team", "96", "--hidden" }
                : new List<string> { FileFor(BattleProject), "--preset", "game", "--framework", "skecs", "--ticks", ticks.ToString(), "--warmup", "600", "--seed", "7331", "--ships-per-team", "96", "--churn", "1", "--output", output };
            if (entry.Paced) arguments.Add(entry.Desktop ? "--profile-paced" : "--realtime");
            await host.RunAsync("dotnet", arguments.ToArray());
            var result = Workspace.Read(output);
            var current = entry.Desktop ? result["Config"] : result["Metadata"]!["Options"];
            if (current!["ChurnMultiplier"]!.GetValue<double>() != 1 || current["Seed"]!.GetValue<int>() != 7331 || current["ShipsPerTeam"]!.GetValue<int>() != 96 || (config is not null && !JsonNode.DeepEquals(config, current)))
                throw new InvalidDataException("Matched desktop study configuration drift: " + entry.Name);
            config = current.DeepClone();
        }
        Environment.SetEnvironmentVariable("DOTNET_TieredCompilation", "0");
        manifest["BattleConfig"] = config;
        AssertStable();
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
        manifest["Completed"] = true; manifest["FinishedAt"] = Now();
        Workspace.Write(Path.Combine(directory, "manifest.json"), manifest);
    }
}
