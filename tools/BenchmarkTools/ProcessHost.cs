using System.Diagnostics;

namespace SKECS.BenchmarkTools;

public sealed class ProcessHost(string root)
{
    public async Task<string> CaptureAsync(string executable, IEnumerable<string> arguments, bool allowFailure = false)
        => await ExecuteAsync(executable, arguments, echo: false, allowFailure);
    public async Task RunAsync(string executable, params string[] arguments)
        => _ = await ExecuteAsync(executable, arguments, echo: true, allowFailure: false);
    private async Task<string> ExecuteAsync(string executable, IEnumerable<string> arguments, bool echo, bool allowFailure)
    {
        var args = arguments.ToArray();
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) when (allowFailure) { return ""; }
        var output = new System.Text.StringBuilder();
        var stdout = Pump(process.StandardOutput, echo ? Console.Out : null, output);
        var stderr = Pump(process.StandardError, echo ? Console.Error : null, null);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException($"{executable} failed ({process.ExitCode}): {string.Join(' ', args)}");
        return process.ExitCode == 0 ? output.ToString().Trim() : "";
    }
    private static async Task Pump(StreamReader reader, TextWriter? destination, System.Text.StringBuilder? captured)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            captured?.AppendLine(line);
            if (destination is not null) await destination.WriteLineAsync(line);
        }
    }
}
