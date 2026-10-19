namespace SKECS.BenchmarkTools;

public static class BenchmarkCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Help(); return 0; }
            string command = args[0];
            string suite = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "all";
            int start = suite == "all" && (args.Length < 2 || args[1].StartsWith('-')) ? 1 : 2;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new HashSet<string>(StringComparer.Ordinal);
            for (int i = start; i < args.Length; i++)
            {
                if (args[i] is "--check" or "--no-build" or "--include-desktop" or "--skip-ticks") flags.Add(args[i]);
                else if (args[i] is "--root" or "--output" or "--data" or "--ticks" or "--warmup")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value: " + args[i]);
                    values.Add(args[i], args[++i]);
                }
                else throw new ArgumentException("Unknown option: " + args[i]);
            }
            var workspace = new Workspace(Workspace.FindRoot(values.GetValueOrDefault("--root")));
            workspace.ConfigureEnvironment();
            switch (command)
            {
                case "generate": return BenchmarkCodeGenerator.Run(workspace.Root, flags.Contains("--check"));
                case "verify-vendor": VendorVerifier.Run(workspace.Root); return 0;
                case "restore-vendor": await VendorRestore.RunAsync(workspace.Root); return 0;
                case "hardware":
                    Workspace.Write(workspace.ArtifactPath(values.GetValueOrDefault("--output", "artifacts/benchmarks/current/hardware.json")), HardwareCapture.Create());
                    return 0;
                case "run":
                case "validate":
                    await new CampaignRunner(workspace).RunAsync(suite, values.GetValueOrDefault("--output"), flags.Contains("--no-build"), flags.Contains("--include-desktop"), command == "validate",
                        int.Parse(values.GetValueOrDefault("--ticks", "108000"), System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(values.GetValueOrDefault("--warmup", "600"), System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                case "report":
                case "verify":
                    if (suite == "composition")
                    {
                        CompositionReports.Run(workspace.Root,
                            workspace.LocalPath(values.GetValueOrDefault("--data", "artifacts/benchmarks/history/docs/space-battle/composition")),
                            workspace.LocalPath(values.GetValueOrDefault("--output", "docs/space-battle/composition")), command == "report");
                        return 0;
                    }
                    if (suite is not ("all" or "comparison" or "supplement" or "space-battle" or "desktop")) throw new ArgumentException("Unknown suite: " + suite);
                    bool render = command == "report";
                    if (suite is "all" or "comparison") MicroReports.Comparison(workspace.Root, Data("micro"), render, Output("micro"));
                    if (suite is "all" or "supplement") MicroReports.Supplement(workspace.Root, Data("micro"), render, Output("micro"));
                    if (suite is "all" or "space-battle") SpaceBattleReports.Run(workspace.Root, Data("space-battle"), !flags.Contains("--skip-ticks"), render, Output("space-battle"));
                    if (suite == "desktop" || (suite == "all" && (flags.Contains("--include-desktop") || File.Exists(Path.Combine(Data("desktop"), "manifest.json")))))
                        DesktopReports.Run(workspace.Root, Data("desktop"), render, Output("desktop"));
                    Console.WriteLine("PASS: requested saved benchmark datasets verified" + (render ? " and reports regenerated." : "."));
                    return 0;
                default: throw new ArgumentException("Unknown command: " + command);
            }

            string Data(string name) => workspace.LocalPath(values.GetValueOrDefault("--data") is { } data
                ? (suite == "all" ? Path.Combine(data, name) : data) : $"artifacts/benchmarks/current/{name}");
            string Output(string name) => workspace.LocalPath(values.GetValueOrDefault("--output") is { } output
                ? (suite == "all" ? Path.Combine(output, name) : output)
                : name == "micro" ? "docs/benchmarks/current" : name == "desktop" ? "docs/space-battle/desktop-current" : "docs/space-battle/current");
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static void Help() => Console.WriteLine("""
        SKECS portable .NET benchmark tools

        dotnet run --project tools/BenchmarkTools -c Release -- <command> [suite] [options]

        generate [--check]            Generate shared benchmark sources, or check freshness.
        verify-vendor                Verify pinned runtime sources and licenses.
        restore-vendor               Recover missing pinned vendor files (read-only downloads).
        hardware                     Capture portable runtime/CPU/memory-limit metadata.
        validate [suite]             Build and validate adapters without measuring.
        run [suite]                  Build, validate, measure sequentially and render reports.
        report [suite]               Regenerate reports/SVG from retained local raw data.
        verify [suite]               Validate retained raw inputs without changing reports.

        Suites: all, comparison, supplement, space-battle, desktop.
        'report composition' / 'verify composition' handle saved component census data.
        'all' is headless; add --include-desktop for the graphics-device study.
        --output <directory>         Raw runs under artifacts/; report destinations project-local.
        --data <directory>           Project-local saved inputs for report/verify.
        --no-build                   Require a matching certified build from an earlier run.
        --ticks N --warmup N          Space Battle smoke overrides; not a full report.
        --skip-ticks                 Skip CSV recomputation (archive integrity still checked).
        --root <directory>           Workspace root (normally discovered automatically).

        Default raw data: artifacts/benchmarks/current (ignored, never committed).
        Published Markdown/SVG: docs/benchmarks/current and docs/space-battle.
        No Python, PowerShell, shell scripts, registry/WMI or graphics library is needed
        by the tools. The optional desktop measurement uses the game's graphics device.
        """);
}
