using System.Globalization;

namespace Skecs.Agentic;

internal sealed record Request(string Operation, string Configuration, int TimeoutSeconds, string[] Arguments)
{
    public static Request Parse(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]) || args[0].StartsWith('-'))
            throw new ArgumentException("Expected run <operation> [arguments] [--configuration Debug|Release] [--timeout-seconds N].");

        var values = new List<string>();
        int i = 1;
        while (i < args.Length && !args[i].StartsWith('-'))
            values.Add(args[i++]);

        string configuration = "Release";
        int timeout = 300;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (i < args.Length)
        {
            string flag = args[i++];
            if ((flag is not ("--configuration" or "--timeout-seconds")) || !seen.Add(flag) || i == args.Length)
                throw new ArgumentException($"Unexpected, repeated or incomplete option: {flag}.");
            string value = args[i++];
            if (flag == "--configuration")
            {
                if (value is not ("Debug" or "Release"))
                    throw new ArgumentException("Configuration must be Debug or Release.");
                configuration = value;
            }
            else
                timeout = Number(value, "timeout-seconds", 1, 3600);
        }
        return new(args[0], configuration, timeout, values.ToArray());
    }

    public static int Number(string value, string name, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
            number < min || number > max || value != number.ToString(CultureInfo.InvariantCulture))
            throw new ArgumentException($"{name} must be an integer from {min} to {max}.");
        return number;
    }
}

internal sealed record Operation(string Id, string Usage, string? Project, string[] Prerequisites, string? UnavailableReason = null)
{
    private const string Core = "SKECS/SKECS.csproj";
    private const string CoreTests = "SKECS.Tests/SKECS.Tests.csproj";
    private const string Benchmarks = "SKECS.Benchmarks/SKECS.Benchmarks.csproj";
    private const string SpaceBattle = "SpaceBattle.Benchmarks/SpaceBattle.Benchmarks.csproj";
    private const string Replay = "SKECS.Determinism/SKECS.Determinism.csproj";
    private const string Profiler = "tools/ProfilerBenchmarks/ProfilerBenchmarks.csproj";
    private const string Profiles = "SKECS.RuntimeProfiles.Tools/SKECS.RuntimeProfiles.Tools.csproj";
    private const string Network = "SKECS.Network.Sample/SKECS.Network.Sample.csproj";

    public static readonly Operation[] Registry =
    [
        new("build", "", Core, []),
        new("test", "", CoreTests, [Core]),
        new("check-comparison", "", Benchmarks, [Core]),
        new("check-space-battle", "", SpaceBattle, [Core, "SpaceBattle.Simulations/SpaceBattle.Simulations.csproj"]),
        new("benchmark-list", "[standard|extended|all|numerics|runner]", Benchmarks, [Core]),
        new("diff", "", null, []),
        new("determinism-run", "", Replay, [Core, "SKECS.Serialization/SKECS.Serialization.csproj", "tools/determinism/fixtures", "tools/determinism/golden.json"]),
        new("determinism-compare", "<expected-replay-directory> <actual-replay-directory>", Replay, [Core, "SKECS.Serialization/SKECS.Serialization.csproj"]),
        new("profile", "<dense|churn> <off|on> <frames> <entities> <event-capacity>", Profiler, [Core, "SKECS.Profiler/SKECS.Profiler.csproj"]),
        new("profile-verify", "<corpus-json>", Profiles, [Core,
            "SKECS.RuntimeProfiles.Tools/Program.cs", "SKECS.RuntimeProfiles/SKECS.RuntimeProfiles.csproj"]),
        new("ga-check", "", null, [], "GA conformance requires separately provisioned native tools and a Vulkan device."),
        new("ga-test", "", "SKECS.GA.Tests/SKECS.GA.Tests.csproj", [Core, "SKECS.GA.Compiler/SKECS.GA.Compiler.csproj"]),
        new("network-check", "[full|delta]", Network, [Core, "SKECS.Network/SKECS.Network.csproj",
            "SKECS.Network.Transport.Loopback/SKECS.Network.Transport.Loopback.csproj"]),
        new("network-test", "", "SKECS.Network.Tests/SKECS.Network.Tests.csproj",
            [Core, "SKECS.Network/SKECS.Network.csproj"])
    ];

    public string? Missing(LocalWorkspace workspace)
    {
        if (UnavailableReason is not null)
            return UnavailableReason;
        if (Project is not null && !File.Exists(workspace.PathFor(Project)))
            return "Missing project: " + Project;
        foreach (string prerequisite in Prerequisites)
        {
            string path = workspace.PathFor(prerequisite);
            if (!File.Exists(path) && !Directory.Exists(path))
                return "Missing prerequisite: " + prerequisite;
        }
        return null;
    }

    public void Validate(Request request)
    {
        string[] args = request.Arguments;
        switch (Id)
        {
            case "benchmark-list" when args is [] or ["standard" or "extended" or "all" or "numerics" or "runner"]:
            case "network-check" when args is [] or ["full" or "delta"]:
            case "determinism-compare" when args.Length == 2:
            case "profile-verify" when args.Length == 1:
                return;
            case "profile" when args is ["dense" or "churn", "off" or "on", var frames, var entities, var capacity]:
                Request.Number(frames, "frames", 1, 10000);
                Request.Number(entities, "entities", 1, 100000);
                Request.Number(capacity, "event-capacity", 1, 1000000);
                return;
        }
        if (args.Length != 0 || Id is "determinism-compare" or "profile" or "profile-verify")
            throw new ArgumentException($"Expected: {Id} {Usage}".TrimEnd());
    }

    public Command[] Commands(Request request, LocalWorkspace workspace, string runDirectory)
    {
        string[] args = request.Arguments;
        string buildPath = workspace.PathFor(LocalWorkspace.BuildOutput);
        string[] framework = Id == "build" ? [] : ["--framework", "net8.0"];
        var build = new Command("build", "dotnet",
            ["build", Project!, "--configuration", request.Configuration, "--no-restore", "--disable-build-servers",
                "--artifacts-path", buildPath, "-m:1", .. framework]);
        Command Cli(string name, params string[] cli) => new(name, "dotnet",
            ["run", "--project", Project!, "--configuration", request.Configuration, "--framework", "net8.0", "--no-build",
                "--artifacts-path", buildPath, "--", .. cli]);

        return Id switch
        {
            "build" => [build],
            "test" or "ga-test" or "network-test" => [new("test", "dotnet",
                ["test", Project!, "--configuration", request.Configuration, "--no-restore", "--disable-build-servers",
                    "--artifacts-path", buildPath, "-m:1",
                    "--results-directory", workspace.PathFor(runDirectory), "--logger", "trx;LogFileName=tests.trx"])],
            "check-comparison" => [build, Cli("comparison", "check", "comparison")],
            "check-space-battle" => [build, Cli("mechanics", "measure", "--validate"), Cli("ports", "measure", "--validate-ports")],
            "benchmark-list" => [build, Cli("discovery", "bdn", "--suite", args.FirstOrDefault() ?? "all", "--list", "flat", "--artifacts", runDirectory + "/bdn")],
            "diff" => [new("diff", "git", [.. LocalWorkspace.GitOptions,
                "diff", "--no-color", "--no-ext-diff", "--no-textconv", "--ignore-submodules=all", "--binary", "HEAD", "--"])],
            "determinism-run" => [build, Cli("replay", "run", "tools/determinism/fixtures", runDirectory + "/replay")],
            "determinism-compare" => [build, Cli("compare", "compare", workspace.ReplayInput(args[0]), workspace.ReplayInput(args[1]))],
            "profile" => [build, Cli("profile", [.. args, Path.GetFileName(runDirectory), runDirectory + "/profile"])],
            "profile-verify" => [build,
                Cli("capture", "capture", runDirectory + "/profile-corpus.json", runDirectory + "/profile-run"),
                Cli("propose", "propose", runDirectory + "/profile-run"),
                Cli("verify", "verify", runDirectory + "/profile-run")],
            "network-check" => [build, Cli("replication", "--transport", "loopback", "--mode",
                args.FirstOrDefault() ?? "delta", "--entities", "32", "--ticks", "4", "--warmup", "1")],
            _ => throw new InvalidOperationException($"Operation has no commands: {Id}.")
        };
    }

}
