using System.Diagnostics;

namespace Skecs.Samples;

internal readonly record struct ToolResult(int ExitCode, string Output, string Error)
{
    public void EnsureSuccess(string command)
    {
        if (ExitCode != 0)
            throw new InvalidOperationException($"{command} exited with code {ExitCode}.\n{Output}{Error}".TrimEnd());
    }
}

internal static class ToolCommand
{
    public static async Task<ToolResult> RunAsync(string assembly, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, assembly));
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {assembly}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ToolResult(process.ExitCode, await output, await error);
    }

    public static string FindCheckout()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SKECS.Samples", "SKECS.Samples.csproj")))
                    return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run this sample from a local SKECS checkout.");
    }
}
