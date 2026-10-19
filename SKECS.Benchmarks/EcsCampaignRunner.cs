using System.Text.Json.Nodes;

namespace SKECS.BenchmarkTools;

public sealed class EcsCampaignRunner(Workspace workspace)
{
    private const string Project = "SKECS.Benchmarks/SKECS.Benchmarks.csproj";
    private readonly CampaignContext context = new(workspace, "ecs");
    private static JsonNode Clone(JsonNode? node) => node?.DeepClone() ?? throw new InvalidDataException("Missing campaign field.");

    public async Task RunAsync(string suite, string? output, bool noBuild, bool validateOnly)
    {
        workspace.ConfigureEnvironment();
        if (suite is not ("all" or "comparison" or "supplement")) throw new ArgumentException("Unknown ECS suite: " + suite);
        if (output is not null) _ = workspace.ArtifactPath(output);
        if (BenchmarkCodeGenerator.Run(workspace.Root, check: true) != 0) throw new InvalidDataException("Generated benchmark sources are stale; run generate.");
        VendorVerifier.Run(workspace.Root);
        await context.BuildAsync([Project], noBuild);
        if (suite is "all" or "comparison")
        {
            await context.Host.RunAsync("dotnet", context.FileFor(Project), "check", "comparison");
            await context.Host.RunAsync("dotnet", context.FileFor(Project), "check", "stress");
        }
        if (suite is "all" or "supplement") await context.Host.RunAsync("dotnet", context.FileFor(Project), "check", "extended");
        context.AssertStable();
        if (validateOnly) { Console.WriteLine("PASS: requested ECS benchmark contracts validated."); return; }
        string data = workspace.ArtifactPath(output ?? "artifacts/benchmarks/current/micro");
        Directory.CreateDirectory(data);
        string reports = output is null ? workspace.PathFor("docs/benchmarks/current") : Path.Combine(data, "reports");
        if (suite is "all" or "comparison") await ComparisonAsync(data, reports);
        if (suite is "all" or "supplement") await SupplementAsync(data, reports);
        context.AssertStable();
        Console.WriteLine("PASS: ECS benchmark campaign complete; raw files are local artifacts.");
    }

    private async Task ComparisonAsync(string directory, string reports)
    {
        string started = CampaignContext.Now();
        Workspace.Write(Path.Combine(directory, "campaign.json"), new JsonObject { ["completed"] = false, ["started"] = started });
        await context.Host.RunAsync("dotnet", context.FileFor(Project), "measure", "comparison", Path.Combine(directory, "base.json"));
        await context.Host.RunAsync("dotnet", context.FileFor(Project), "measure", "stress", Path.Combine(directory, "stress.json"));
        context.AssertStable();
        var manifest = context.Common(started);
        manifest["completed"] = true;
        manifest["workingTreeModified"] = context.Dirty;
        manifest["gitMetadataAvailable"] = context.Revision != "source-tree";
        manifest["measurementAssemblySha256"] = Workspace.Hash(context.FileFor(Project));
        manifest["skecsAssemblySha256"] = Workspace.Hash(Path.Combine(Path.GetDirectoryName(context.FileFor(Project))!, "SKECS.dll"));
        manifest["baseInputSha256"] = Workspace.Hash(Path.Combine(directory, "base.json"));
        manifest["stressInputSha256"] = Workspace.Hash(Path.Combine(directory, "stress.json"));
        manifest["matrix"] = new JsonObject { ["base"] = 40, ["stress"] = 70, ["frameworks"] = 5 };
        manifest["methodology"] = new JsonObject { ["baseSamples"] = 20, ["stressSamples"] = 8, ["seed"] = 2048, ["singleThread"] = true, ["tiering"] = "off", ["caseOrder"] = "deterministically shuffled within each suite" };
        manifest["validation"] = new JsonObject { ["base"] = "passed before timing", ["stress"] = "passed before timing and after each stress case" };
        manifest["command"] = "dotnet run --project SKECS.Benchmarks -c Release -- run comparison";
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
            if (prior["sourceRevision"]?.GetValue<string>() != context.Revision || !JsonNode.DeepEquals(prior["sourceHashes"], Workspace.HashObject(context.SourceHashes)))
                throw new InvalidDataException("Comparison belongs to a different source snapshot. Run comparison first, or use a fresh --output for supplement.");
            foreach (var pair in prior["assemblyHashes"]!.AsObject().Where(p => p.Key.StartsWith("SKECS.Benchmarks/bin/Release/net8.0/", StringComparison.Ordinal)))
                if (!context.BinaryHashes.TryGetValue(pair.Key, out var digest) || !string.Equals(pair.Value!.GetValue<string>(), digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Comparison binary differs; run comparison first or use a fresh --output: " + pair.Key);
        }
        string started = CampaignContext.Now();
        Workspace.Write(Path.Combine(directory, "supplement.json"), new JsonObject { ["completed"] = false, ["started"] = started });
        await context.Host.RunAsync("dotnet", context.FileFor(Project), "measure", "api", Path.Combine(directory, "optimized-api.json"));
        await context.Host.RunAsync("dotnet", context.FileFor(Project), "measure", "storage", Path.Combine(directory, "storage.json"));
        foreach (var (name, suite, expected) in new[] { ("skecs", "standard", 34), ("legacy", "extended", 51) })
        {
            string suiteStarted = CampaignContext.Now();
            string artifacts = Path.Combine(directory, "bdn-work", name);
            string rawDirectory = Path.Combine(directory, "bdn-raw", name);
            Directory.CreateDirectory(rawDirectory);
            // Each run gets a distinct work folder, so old partial exporters cannot join this campaign.
            artifacts = Path.Combine(artifacts, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffff"));
            await context.Host.RunAsync("dotnet", context.FileFor(Project), "bdn", "--suite", suite, "--filter", "*", "--job", "Short", "--iterationCount", "8", "--warmupCount", "3", "--inProcess", "--exporters", "fulljson", "--artifacts", artifacts);
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
            context.AssertStable();
            var metadata = new JsonObject
            {
                ["sourceRevision"] = context.Revision, ["sourceHashes"] = Workspace.HashObject(context.SourceHashes),
                ["configuration"] = "Release", ["tieredCompilation"] = "0", ["job"] = "Short", ["toolchain"] = "InProcess",
                ["warmupCount"] = 3, ["iterationCount"] = 8, ["runtime"] = Clone(last!["HostEnvironmentInfo"]!["RuntimeVersion"]),
                ["assemblySha256"] = Workspace.Hash(context.FileFor(Project)), ["started"] = suiteStarted, ["finished"] = CampaignContext.Now(),
                ["expectedCases"] = expected, ["buildSkipped"] = context.BuildSkipped,
                ["note"] = "All raw iterations retained; BenchmarkDotNet statistics use its configured outlier policy. Five-million-entity stress is opt-in."
            };
            Workspace.Write(Path.Combine(directory, $"bdn-{name}.json"), new JsonObject { ["metadata"] = metadata, ["Benchmarks"] = rows, ["rawReports"] = rawReports });
        }
        context.AssertStable();
        var manifest = context.Common(started);
        manifest["completed"] = true;
        manifest["comparisonAssemblySha256"] = Workspace.Hash(context.FileFor(Project));
        var inputs = new JsonObject();
        foreach (var name in new[] { "optimized-api.json", "storage.json", "bdn-skecs.json", "bdn-legacy.json" }) inputs[name] = Workspace.Hash(Path.Combine(directory, name));
        manifest["inputHashes"] = inputs;
        manifest["command"] = "dotnet run --project SKECS.Benchmarks -c Release -- run supplement";
        Workspace.Write(Path.Combine(directory, "supplement.json"), manifest);
        try { MicroReports.Supplement(workspace.Root, directory, render: true, outputDirectory: reports); }
        catch { manifest["completed"] = false; Workspace.Write(Path.Combine(directory, "supplement.json"), manifest); throw; }
    }

}
