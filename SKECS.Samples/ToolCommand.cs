using System.Diagnostics;

namespace Skecs.Samples;

internal readonly record struct ToolResult(int ExitCode, string Output, string Error)
{
    public void EnsureSuccess(string command)
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} exited with code {ExitCode}.\n{Output}{Error}".TrimEnd());
        }
    }
}

internal static class ToolCommand
{
    public static async Task<ToolResult> RunAsync(
        string assembly,
        string workingDirectory,
        params string[] arguments)
    {
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, assembly));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {assembly}.");

        // Drain both streams while the process runs so neither pipe can block it.
        var readOutput = process.StandardOutput.ReadToEndAsync();
        var readError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ToolResult(process.ExitCode, await readOutput, await readError);
    }

    public static string FindCheckout()
    {
        foreach (var startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                var project = Path.Combine(directory.FullName, "SKECS.Samples", "SKECS.Samples.csproj");
                if (File.Exists(project))
                {
                    return directory.FullName;
                }
            }
        }

        throw new InvalidOperationException("Run this sample from a local SKECS checkout.");
    }
}
