using System.Text.Json;

namespace Skecs.Samples.Agentic;

internal static class Sample
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("Usage: agentic");

        var checkout = ToolCommand.FindCheckout();
        var catalog = await ToolCommand.RunAsync("Skecs.Agentic.dll", checkout, "list");
        catalog.EnsureSuccess("agentic list");
        using var listing = JsonDocument.Parse(catalog.Output);
        var test = listing.RootElement.GetProperty("operations").EnumerateArray()
            .Single(operation => operation.GetProperty("id").GetString() == "test");
        if (!test.GetProperty("available").GetBoolean())
            throw new InvalidOperationException($"The test operation is unavailable: {test.GetProperty("reason").GetString()}");
        Console.WriteLine($"Discovered test operation for {test.GetProperty("project").GetString()}.");

        var check = await ToolCommand.RunAsync("Skecs.Agentic.dll", checkout, "run", "test");
        if (string.IsNullOrWhiteSpace(check.Output))
            check.EnsureSuccess("agentic run test");
        using var result = JsonDocument.Parse(check.Output);
        var report = result.RootElement;
        var status = report.GetProperty("status").GetString();
        var resultFile = report.GetProperty("resultFile").GetString();
        var trx = report.GetProperty("artifacts").EnumerateArray()
            .Select(artifact => artifact.GetString())
            .SingleOrDefault(path => path?.EndsWith("/tests.trx", StringComparison.Ordinal) == true);
        Console.WriteLine($"Exit code: {check.ExitCode}. Status: {status}.");
        if (resultFile is not null)
            Console.WriteLine($"Result JSON: {resultFile}");
        if (trx is not null)
            Console.WriteLine($"TRX report: {trx}");

        if (check.ExitCode != 0 || status != "passed")
            throw new InvalidOperationException(report.GetProperty("error").GetString() ?? "The test operation failed.");
        if (resultFile is null || trx is null ||
            !File.Exists(Path.Combine(checkout, resultFile)) || !File.Exists(Path.Combine(checkout, trx)))
            throw new InvalidOperationException("The test operation did not produce its result JSON and TRX report.");
    }
}
