using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SKECS.GA;
using SKECS.GA.Benchmarks;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Run from the workspace root with probes built under artifacts/ga/probe.
        Ga.Benchmarks smoke <hardware device index> [rows]
        Ga.Benchmarks dry <hardware device index>
        Ga.Benchmarks benchmark <hardware device index>
        Smoke validates a single dispatch and prints sample duration and process managed allocations.
        Dry runs BenchmarkDotNet's Dry job for a 65-row dispatch. Benchmark runs the full parameter set.
        """);
    return 0;
}

int rows = 65;
if (args.Length is < 2 or > 3 || args[0] is not ("smoke" or "dry" or "benchmark") ||
    !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int deviceIndex) ||
    args.Length == 3 && (args[0] != "smoke" || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out rows) || rows == 0))
{
    Console.Error.WriteLine("Pass smoke, dry or benchmark and an explicit nonnegative hardware Vulkan device index. Run with --help for arguments.");
    return 2;
}

try
{
    string workspace = Directory.GetCurrentDirectory();
    if (!File.Exists(Path.Combine(workspace, "tools", "ga", "benchmarks", "Ga.Benchmarks.csproj")))
        throw new InvalidOperationException("Run Ga.Benchmarks from the workspace root.");

    string directory = Path.Combine(workspace, "artifacts", "ga", "benchmarks");
    string nativeDirectory = Path.Combine(workspace, "artifacts", "ga", "probe");
    using var target = await PipelineTarget.Create(nativeDirectory, directory, deviceIndex);

    Console.WriteLine("Standalone GA primitive pipeline. Timings start with already packed input rows.");
    Console.WriteLine($"Host {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"RequireGpu selected Vulkan device {deviceIndex}, {target.Device.Device}, API {target.Device.ApiVersion}, driver {target.Device.Driver}.");
    Console.WriteLine($"Compute profile {target.ComputeProfile}, qualification {target.Qualification}, artifact {target.ArtifactId}.");
    if (args[0] == "smoke")
    {
        var workload = new PipelineWorkload(target, rows);
        workload.RunSmoke();
        return 0;
    }

    bool dry = args[0] == "dry";
    string reports = Path.Combine(directory, dry ? "dry" : "benchmarkdotnet");
    MixedPipelineBenchmarks.Target = target;
    MixedPipelineBenchmarks.RowCounts = dry ? [65] : [65, 4096, 65536];
    var job = (dry ? Job.Dry : Job.Default).WithToolchain(InProcessEmitToolchain.Instance).DontEnforcePowerPlan();
    var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job)
        .WithArtifactsPath(reports);
    var summary = BenchmarkRunner.Run<MixedPipelineBenchmarks>(config);
    if (summary.Reports.Length == 0 || summary.Reports.Any(report => !report.Success))
        return 1;

    string metadata = JsonSerializer.Serialize(new
    {
        Mode = args[0],
        DeviceIndex = deviceIndex,
        target.Device,
        target.ComputeProfile,
        Qualification = target.Qualification.ToString(),
        target.ArtifactId,
        OS = RuntimeInformation.OSDescription,
        Runtime = RuntimeInformation.FrameworkDescription,
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Dispatch = MixedPipelineBenchmarks.RowCounts.Select(count => new
        {
            Rows = count,
            WordsPerRow = target.Program.WordCount,
            Submissions = 1,
            Workgroups = (count + GaSlangModule.ThreadsPerGroup - 1) / GaSlangModule.ThreadsPerGroup,
            LanesPerWorkgroup = GaSlangModule.ThreadsPerGroup
        })
    }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(reports, "dispatch-metadata.json"), metadata + "\n");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
