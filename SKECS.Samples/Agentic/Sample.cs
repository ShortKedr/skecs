using System.Text.Json;

namespace Skecs.Samples.Agentic;

internal static class Sample
{
    private const string AgenticAssembly = "Skecs.Agentic.dll";

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException("Usage: agentic");
        }

        var checkout = ToolCommand.FindCheckout();
        await ShowTestOperationAsync(checkout);

        var testRun = await ToolCommand.RunAsync(AgenticAssembly, checkout, "run", "test");
        ShowTestReport(checkout, testRun);
    }

    private static async Task ShowTestOperationAsync(string checkout)
    {
        var catalog = await ToolCommand.RunAsync(AgenticAssembly, checkout, "list");
        catalog.EnsureSuccess("agentic list");

        using var listing = JsonDocument.Parse(catalog.Output);
        var operations = listing.RootElement.GetProperty("operations").EnumerateArray();
        var test = operations
            .Single(operation => operation.GetProperty("id").GetString() == "test");
        if (!test.GetProperty("available").GetBoolean())
        {
            var reason = test.GetProperty("reason").GetString();
            throw new InvalidOperationException($"The test operation is unavailable: {reason}");
        }

        var project = test.GetProperty("project").GetString();
        Console.WriteLine($"Discovered test operation for {project}.");
    }

    private static void ShowTestReport(string checkout, ToolResult testRun)
    {
        if (string.IsNullOrWhiteSpace(testRun.Output))
        {
            testRun.EnsureSuccess("agentic run test");
        }

        using var result = JsonDocument.Parse(testRun.Output);
        var report = result.RootElement;
        var status = report.GetProperty("status").GetString();
        var resultFile = report.GetProperty("resultFile").GetString();
        var trxFile = report.GetProperty("artifacts")
            .EnumerateArray()
            .Select(artifact => artifact.GetString())
            .SingleOrDefault(path => path?.EndsWith("/tests.trx", StringComparison.Ordinal) == true);

        // A failed test run may still have reports worth inspecting.
        Console.WriteLine($"Exit code: {testRun.ExitCode}. Status: {status}.");
        if (resultFile is not null)
        {
            Console.WriteLine($"Result JSON: {resultFile}");
        }

        if (trxFile is not null)
        {
            Console.WriteLine($"TRX report: {trxFile}");
        }

        if (testRun.ExitCode != 0 || status != "passed")
        {
            var error = report.GetProperty("error").GetString();
            throw new InvalidOperationException(error ?? "The test operation failed.");
        }

        if (resultFile is null ||
            trxFile is null ||
            !File.Exists(Path.Combine(checkout, resultFile)) ||
            !File.Exists(Path.Combine(checkout, trxFile)))
        {
            throw new InvalidOperationException("The test operation did not produce its result JSON and TRX report.");
        }
    }
}
