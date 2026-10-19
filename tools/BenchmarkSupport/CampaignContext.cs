using System.Text.Json.Nodes;

namespace SKECS.BenchmarkTools;

/// <summary>Build certification and immutable measurement provenance shared by both benchmark targets.</summary>
public sealed class CampaignContext(Workspace workspace, string certificationName = "benchmark-build")
{
    public Workspace Workspace { get; } = workspace;
    public ProcessHost Host { get; } = new(workspace.Root);
    public SortedDictionary<string, string> SourceHashes { get; private set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> BinaryHashes { get; private set; } = new(StringComparer.Ordinal);
    public string Revision { get; private set; } = "source-tree";
    public string Sdk { get; private set; } = "";
    public bool Dirty { get; private set; }
    public bool BuildSkipped { get; private set; }
    public static string Now() => DateTimeOffset.UtcNow.ToString("O");
    public string FileFor(string project) => Workspace.PathFor(Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net8.0", Path.GetFileNameWithoutExtension(project) + ".dll"));

    public async Task BuildAsync(IEnumerable<string> projects, bool noBuild)
    {
        var selected = projects.Distinct(StringComparer.Ordinal).ToArray();
        Workspace.ConfigureEnvironment();
        SourceHashes = Workspace.Sources();
        BuildSkipped = noBuild;
        if (certificationName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || certificationName.Contains('/') || certificationName.Contains('\\'))
            throw new ArgumentException("Invalid build certification name.");
        string snapshot = Workspace.ArtifactPath($"artifacts/benchmarks/build/{certificationName}.json");
        if (noBuild)
        {
            if (!File.Exists(snapshot)) throw new InvalidDataException("No certified benchmark build. Run without --no-build first.");
            var build = Workspace.Read(snapshot);
            if (!JsonNode.DeepEquals(build["SourceHashes"], Workspace.HashObject(SourceHashes))) throw new InvalidDataException("Sources differ from the certified build; run without --no-build.");
            var recorded = build["Projects"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            if (selected.Any(p => !recorded.Contains(p))) throw new InvalidDataException("The certified build does not include this suite.");
            foreach (var pair in build["AssemblyHashes"]!.AsObject())
                if (!File.Exists(Workspace.PathFor(pair.Key)) || !string.Equals(Workspace.Hash(Workspace.PathFor(pair.Key)), pair.Value!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Certified binary changed: " + pair.Key);
        }
        else
        {
            foreach (var project in selected)
            {
                await Host.RunAsync("dotnet", "restore", project, "--ignore-failed-sources", "--disable-parallel", "-p:NuGetAudit=false");
                await Host.RunAsync("dotnet", "build", project, "-c", "Release", "--no-restore", "-m:1", "-nr:false", "-p:GeneratePackageOnBuild=false", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false");
            }
        }
        Workspace.AssertHashes(SourceHashes);
        if (!JsonNode.DeepEquals(Workspace.HashObject(SourceHashes), Workspace.HashObject(Workspace.Sources()))) throw new InvalidDataException("Source inventory changed during compilation.");
        BinaryHashes = Workspace.Binaries(selected);
        if (!noBuild) Workspace.Write(snapshot, new JsonObject { ["SourceHashes"] = Workspace.HashObject(SourceHashes), ["AssemblyHashes"] = Workspace.HashObject(BinaryHashes), ["Projects"] = new JsonArray(selected.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) });
        Revision = await Host.CaptureAsync("git", ["rev-parse", "HEAD"], allowFailure: true);
        if (Revision.Length == 0) Revision = "source-tree";
        Dirty = Revision == "source-tree" || (await Host.CaptureAsync("git", ["status", "--porcelain", "--untracked-files=normal"], allowFailure: true)).Length > 0;
        Sdk = await Host.CaptureAsync("dotnet", ["--version"]);
        Environment.SetEnvironmentVariable("SKECS_BENCHMARK_REVISION", Revision);
        Environment.SetEnvironmentVariable("SKECS_STRESS_EXTREME", "0");
        Environment.SetEnvironmentVariable("SKECS_BENCHMARK_SCENARIOS", null);
    }

    public void AssertStable()
    {
        Workspace.AssertHashes(SourceHashes);
        Workspace.AssertHashes(BinaryHashes);
        if (!JsonNode.DeepEquals(Workspace.HashObject(SourceHashes), Workspace.HashObject(Workspace.Sources()))) throw new InvalidDataException("Source inventory changed during measurement.");
    }

    public JsonObject Common(string started) => new()
    {
        ["format"] = 2, ["started"] = started, ["finished"] = Now(), ["sourceRevision"] = Revision,
        ["sourceHashes"] = Workspace.HashObject(SourceHashes), ["assemblyHashes"] = Workspace.HashObject(BinaryHashes),
        ["buildSkipped"] = BuildSkipped, ["sdk"] = Sdk, ["cpu"] = Workspace.CpuName(), ["logicalProcessors"] = Environment.ProcessorCount
    };
}
