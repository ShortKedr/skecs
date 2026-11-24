using System.Text.Json.Nodes;

namespace Skecs.Samples.Determinism;

internal static class Sample
{
    private const int ChangedStep = 1;

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException("Usage: determinism");
        }

        var checkout = ToolCommand.FindCheckout();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Determinism", "fixtures");
        var runId = Guid.NewGuid().ToString("N");
        var output = Path.Combine(checkout, "artifacts", "samples", "determinism", runId);
        var firstRun = Path.Combine(output, "first");
        var secondRun = Path.Combine(output, "second");
        var divergentRun = Path.Combine(output, "divergent");

        foreach (var run in new[] { firstRun, secondRun })
        {
            var replay = await ReplayAsync("run", fixtures, run);
            replay.EnsureSuccess("determinism run");
            Console.Write(replay.Output);
        }

        var match = await ReplayAsync("compare", firstRun, secondRun);
        match.EnsureSuccess("determinism compare");
        Console.Write(match.Output);

        CreateDivergentTrace(secondRun, divergentRun);
        var mismatch = await ReplayAsync("compare", firstRun, divergentRun);
        var diagnostic = mismatch.Error.Split('\n', 2)[0].TrimEnd('\r');
        var expectedLocation = $"query-order, step {ChangedStep} (";
        if (mismatch.ExitCode != 1 ||
            !diagnostic.StartsWith(expectedLocation, StringComparison.Ordinal) ||
            !diagnostic.Contains("result", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected the first divergence at step {ChangedStep}.\n{mismatch.Output}{mismatch.Error}");
        }

        Console.WriteLine($"Detected intentional divergence at {diagnostic}");
        Console.WriteLine($"Replay traces: {Path.GetRelativePath(checkout, output)}");

        Task<ToolResult> ReplayAsync(params string[] command) =>
            ToolCommand.RunAsync("SKECS.Determinism.dll", checkout, command);
    }

    private static void CreateDivergentTrace(string sourceRun, string divergentRun)
    {
        // Reuse the manifest so the two traces have comparable metadata.
        const string manifestFile = "manifest.json";
        Directory.CreateDirectory(divergentRun);
        File.Copy(Path.Combine(sourceRun, manifestFile), Path.Combine(divergentRun, manifestFile));

        // Each line represents one replay step, beginning with step zero.
        const string traceFile = "query-order.jsonl";
        var steps = File.ReadAllLines(Path.Combine(sourceRun, traceFile));
        var changedStep = JsonNode.Parse(steps[ChangedStep])
            ?? throw new InvalidDataException($"Missing replay step {ChangedStep}.");
        var result = changedStep["result"]?.AsArray()
            ?? throw new InvalidDataException($"Missing result for replay step {ChangedStep}.");
        result.Add("sample divergence");

        steps[ChangedStep] = changedStep.ToJsonString();
        File.WriteAllLines(Path.Combine(divergentRun, traceFile), steps);
    }
}
