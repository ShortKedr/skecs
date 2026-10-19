using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using SKECS.BenchmarkTools;

return await EcsBenchmarkCli.RunAsync(args);

public static class EcsBenchmarkCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("""
                    SKECS ECS benchmarks (.NET 8)
                    dotnet run --project SKECS.Benchmarks -c Release -- <command> [suite] [options]
                    generate [--check]        Generate native common workloads/adapters.
                    verify-vendor             Check pinned runtime source/license bytes.
                    restore-vendor            Recover missing pinned runtime sources.
                    run [all|comparison|supplement]       Build, validate, measure and report.
                    validate [all|comparison|supplement]  Build and check without timing.
                    report|verify [all|comparison|supplement]  Analyze saved raw artifacts.
                    bdn --suite standard|extended|all <BenchmarkDotNet options>
                    check comparison|stress|extended     Isolated correctness validation.
                    measure comparison|stress|api|storage <artifacts/...json>
                    --output <artifacts/directory>  Raw measurement destination.
                    --data <directory> --output <directory>  Saved analysis inputs/output.
                    --root <directory> --no-build
                    Raw JSON/exporters remain ignored under artifacts/. SpaceBattle uses SpaceBattle.Benchmarks.
                    """);
                return 0;
            }
            // BDN receives its own command line unchanged after the explicit suite selector.
            if (args[0] == "bdn")
            {
                var forwarded = args.Skip(1).ToList();
                int position = forwarded.IndexOf("--suite");
                string suite = "all";
                if (position >= 0)
                {
                    if (position + 1 >= forwarded.Count) throw new ArgumentException("Missing BDN suite.");
                    suite = forwarded[position + 1]; forwarded.RemoveRange(position, 2);
                }
                var bdnWorkspace = new Workspace(Workspace.FindRoot());
                for (int i = 0; i < forwarded.Count; i++)
                    if (forwarded[i].StartsWith("--artifacts=", StringComparison.Ordinal))
                    {
                        string path = forwarded[i]["--artifacts=".Length..];
                        forwarded[i] = "--artifacts"; forwarded.Insert(i + 1, path);
                    }
                if (forwarded.Count(value => value == "--artifacts") > 1) throw new ArgumentException("Repeated BDN artifacts directory.");
                int artifacts = forwarded.IndexOf("--artifacts");
                if (artifacts >= 0)
                {
                    if (artifacts + 1 >= forwarded.Count) throw new ArgumentException("Missing BDN artifacts directory.");
                    forwarded[artifacts + 1] = bdnWorkspace.ArtifactPath(forwarded[artifacts + 1]);
                }
                else forwarded.AddRange(["--artifacts", bdnWorkspace.ArtifactPath("artifacts/benchmarks/bdn")]);
                var summaries = BenchmarkSwitcher.FromTypes(EcsBenchmarkSuites.Types(suite)).Run(forwarded.ToArray(), DefaultConfig.Instance).ToArray();
                if (forwarded.Any(value => value is "--list" or "-l" or "--help" or "-h" or "--info" or "--version" || value.StartsWith("--list=", StringComparison.Ordinal))) return 0;
                return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors && summary.Reports.Length > 0 && summary.Reports.All(report => report.Success)) ? 0 : 1;
            }
            if (args[0] is "measure" or "check") return Isolated(args);
            var options = BenchmarkOptions.Parse(args);
            if (options.Flags.Contains("--include-desktop") || options.Flags.Contains("--skip-ticks") || options.Values.ContainsKey("--ticks") || options.Values.ContainsKey("--warmup"))
                throw new ArgumentException("SpaceBattle options belong to SpaceBattle.Benchmarks.");
            var workspace = new Workspace(Workspace.FindRoot(options.Values.GetValueOrDefault("--root")));
            workspace.ConfigureEnvironment();
            switch (options.Command)
            {
                case "generate": return BenchmarkCodeGenerator.Run(workspace.Root, options.Flags.Contains("--check"));
                case "verify-vendor": VendorVerifier.Run(workspace.Root); return 0;
                case "restore-vendor": await VendorRestore.RunAsync(workspace.Root); return 0;
                case "run":
                case "validate":
                    await new EcsCampaignRunner(workspace).RunAsync(options.Suite, options.Values.GetValueOrDefault("--output"), options.Flags.Contains("--no-build"), options.Command == "validate");
                    return 0;
                case "report":
                case "verify":
                    if (options.Suite is not ("all" or "comparison" or "supplement")) throw new ArgumentException("Unknown ECS suite: " + options.Suite);
                    string directory = workspace.LocalPath(options.Values.GetValueOrDefault("--data", "artifacts/benchmarks/current/micro"));
                    string output = workspace.LocalPath(options.Values.GetValueOrDefault("--output", "docs/benchmarks/current"));
                    bool render = options.Command == "report";
                    if (options.Suite is "all" or "comparison") MicroReports.Comparison(workspace.Root, directory, render, output);
                    if (options.Suite is "all" or "supplement") MicroReports.Supplement(workspace.Root, directory, render, output);
                    Console.WriteLine("PASS: saved ECS measurements verified" + (render ? " and reports generated." : "."));
                    return 0;
                default: throw new ArgumentException("Unknown ECS benchmark command: " + options.Command);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            Console.Error.WriteLine("FAIL: " + error.Message); return 1;
        }
    }
    static int Isolated(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Missing isolated suite.");
        bool timing = args[0] == "measure";
        if (timing && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0")
            throw new InvalidOperationException("Start isolated measurements with DOTNET_TieredCompilation=0; run campaigns to configure this automatically.");
        var workspace = new Workspace(Workspace.FindRoot());
        workspace.ConfigureEnvironment();
        if (!timing)
        {
            if (args.Length != 2) throw new ArgumentException("check accepts only a suite.");
            switch (args[1])
            {
                case "comparison": ComparisonRunner.Run(["--validate"]); break;
                case "stress": StressRunner.Handle(["--validate-stress"]); break;
                case "extended": SKECS.Benchmarks.Extended.ExtendedValidation.Run(); break;
                default: throw new ArgumentException("Unknown validation suite: " + args[1]);
            }
            return 0;
        }
        if (args.Length != 3) throw new ArgumentException("measure requires one suite and an artifacts/...json output.");
        string path = workspace.ArtifactPath(args[2]);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Measurement output must be JSON.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        switch (args[1])
        {
            case "comparison": ComparisonRunner.Run([path]); break;
            case "stress": StressRunner.Handle(["--stress", path]); break;
            case "api": OptimizedApiRunner.Handle(["--optimized-api", path]); break;
            case "storage": StorageRunner.Handle(["--storage", path]); break;
            default: throw new ArgumentException("Unknown measurement suite: " + args[1]);
        }
        return 0;
    }
}

public static class EcsBenchmarkSuites
{
    public static Type[] Types(string suite)
    {
        string[] namespaces = suite switch
        {
            "standard" => ["SKECS.Benchmarks"],
            "extended" => ["SKECS.Benchmarks.Extended"],
            "all" => ["SKECS.Benchmarks", "SKECS.Benchmarks.Extended"],
            _ => throw new ArgumentException("Unknown BDN suite: " + suite)
        };
        return typeof(EcsBenchmarkSuites).Assembly.GetTypes().Where(type => type.IsPublic && !type.IsAbstract &&
            namespaces.Contains(type.Namespace) && type.GetMethods().Any(method => method.IsDefined(typeof(BenchmarkAttribute), inherit: true)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
    }
}
