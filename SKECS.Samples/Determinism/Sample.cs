using System.Text.Json.Nodes;

namespace Skecs.Samples.Determinism;

internal static class Sample
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("Usage: determinism");

        var checkout = ToolCommand.FindCheckout();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Determinism", "fixtures");
        var output = Path.Combine(checkout, "artifacts", "samples", "determinism", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(output, "first");
        var second = Path.Combine(output, "second");
        var divergent = Path.Combine(output, "divergent");

        Task<ToolResult> ReplayAsync(params string[] command) =>
            ToolCommand.RunAsync("SKECS.Determinism.dll", checkout, command);

        foreach (var run in new[] { first, second })
        {
            var replay = await ReplayAsync("run", fixtures, run);
            replay.EnsureSuccess("determinism run");
            Console.Write(replay.Output);
        }

        var match = await ReplayAsync("compare", first, second);
        match.EnsureSuccess("determinism compare");
        Console.Write(match.Output);

        // Keep both successful runs intact and introduce one change in a third trace.
        Directory.CreateDirectory(divergent);
        File.Copy(Path.Combine(second, "manifest.json"), Path.Combine(divergent, "manifest.json"));
        const string traceFile = "query-order.jsonl";
        const int changedStep = 1;
        var lines = File.ReadAllLines(Path.Combine(second, traceFile));
        var changed = JsonNode.Parse(lines[changedStep])!.AsObject();
        changed["result"]!.AsArray().Add("sample divergence");
        lines[changedStep] = changed.ToJsonString();
        File.WriteAllLines(Path.Combine(divergent, traceFile), lines);

        var mismatch = await ReplayAsync("compare", first, divergent);
        var diagnostic = mismatch.Error.Split('\n', 2)[0].TrimEnd('\r');
        if (mismatch.ExitCode != 1 ||
            !diagnostic.StartsWith($"query-order, step {changedStep} (", StringComparison.Ordinal) ||
            !diagnostic.Contains("result", StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected the first divergence at step {changedStep}.\n{mismatch.Output}{mismatch.Error}");

        Console.WriteLine($"Detected intentional divergence at {diagnostic}");
        Console.WriteLine($"Replay traces: {Path.GetRelativePath(checkout, output)}");
    }
}
