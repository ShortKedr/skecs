using System.Globalization;
using SKECS.BenchmarkTools;

namespace SpaceBattle.Benchmarks;

public static class BattleCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Help(); return 0; }
            var options = BenchmarkOptions.Parse(args);
            var values = options.Values;
            var flags = options.Flags;
            var suite = options.Suite;
            var workspace = new Workspace(Workspace.FindRoot(values.GetValueOrDefault("--root")));
            workspace.ConfigureEnvironment();
            switch (options.Command)
            {
                case "hardware":
                    Workspace.Write(workspace.ArtifactPath(values.GetValueOrDefault("--output", "artifacts/benchmarks/current/hardware.json")), HardwareCapture.Create());
                    return 0;
                case "run":
                case "validate":
                    await new BattleCampaignRunner(workspace).RunAsync(suite, values.GetValueOrDefault("--output"), flags.Contains("--no-build"), flags.Contains("--include-desktop"), options.Command == "validate",
                        int.Parse(values.GetValueOrDefault("--ticks", "108000"), CultureInfo.InvariantCulture),
                        int.Parse(values.GetValueOrDefault("--warmup", "600"), CultureInfo.InvariantCulture));
                    return 0;
                case "report":
                case "verify":
                    bool render = options.Command == "report";
                    if (suite == "composition")
                    {
                        if (!values.TryGetValue("--data", out var compositionData))
                            throw new ArgumentException("Composition analysis requires --data <saved-census-directory>.");
                        CompositionReports.Run(workspace.Root,
                            workspace.LocalPath(compositionData),
                            workspace.LocalPath(values.GetValueOrDefault("--output", "artifacts/benchmarks/composition-reports")), render);
                        return 0;
                    }
                    if (suite is not ("all" or "space-battle" or "desktop")) throw new ArgumentException("Unknown SpaceBattle suite: " + suite);
                    if (suite is "all" or "space-battle") SpaceBattleReports.Run(workspace.Root, Data("space-battle"), !flags.Contains("--skip-ticks"), render, Output("space-battle"));
                    if (suite == "desktop" || (suite == "all" && (flags.Contains("--include-desktop") || File.Exists(Path.Combine(Data("desktop"), "manifest.json")))))
                        DesktopReports.Run(workspace.Root, Data("desktop"), render, Output("desktop"));
                    Console.WriteLine("PASS: saved SpaceBattle datasets verified" + (render ? " and reports regenerated." : "."));
                    return 0;
                default: throw new ArgumentException("Unknown command: " + options.Command);
            }

            string Data(string name) => workspace.LocalPath(values.GetValueOrDefault("--data") is { } data
                ? (suite == "all" ? Path.Combine(data, name) : data) : $"artifacts/benchmarks/current/{name}");
            string Output(string name) => workspace.LocalPath(values.GetValueOrDefault("--output") is { } output
                ? (suite == "all" ? Path.Combine(output, name) : output)
                : name == "desktop" ? "docs/space-battle/desktop-current" : "docs/space-battle/current");
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static void Help() => Console.WriteLine("""
        SpaceBattle benchmarks and analysis

        dotnet run --project SpaceBattle.Benchmarks -c Release -- <command> [suite] [options]

        validate [suite]             Build and validate adapters and gameplay contracts.
        run [suite]                  Build, validate, measure sequentially and render reports.
        report [suite]               Regenerate Markdown/SVG from retained local raw data.
        verify [suite]               Validate retained raw data without changing reports.
        hardware                     Capture portable runtime/CPU/memory-limit metadata.
        measure <measurement flags>  Run one measurement or a focused --validate-* check.

        Suites: all, space-battle, desktop. Saved census analysis: report/verify composition.
        'all' measures headless; add --include-desktop for the graphics-device study.
        --output <directory>         Raw runs under artifacts/; reports project-local.
        --data <directory>           Saved inputs for report/verify.
        --no-build                   Require a matching certified build.
        --ticks N --warmup N          Smoke overrides; use a separate artifact output.
        --skip-ticks                 Skip tick recomputation, retaining archive checks.
        --root <directory>           Workspace root, normally discovered automatically.
        """);
}
