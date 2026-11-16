using System.ComponentModel;
using System.Diagnostics;

namespace Skecs.Agentic;

internal sealed record Command(string Name, string Executable, string[] Arguments);
internal sealed record Step(string Name, string Executable, string[] Arguments, int? ExitCode, long ElapsedMilliseconds,
    string Stdout, string Stderr, string? Error);

internal sealed class ProcessRunner(LocalWorkspace workspace, string output, Dictionary<string, string> environment, List<Step> steps)
{
    public async Task<Step> ExecuteAsync(Command command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string stdout = $"{output}/{command.Name}.stdout.log";
        string stderr = $"{output}/{command.Name}.stderr.log";
        var start = new ProcessStartInfo(command.Executable)
        {
            WorkingDirectory = workspace.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in command.Arguments)
            start.ArgumentList.Add(argument);
        foreach (string key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE" })
            start.Environment.Remove(key);
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        await using var stdoutFile = File.Open(workspace.PathFor(stdout), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var stderrFile = File.Open(workspace.PathFor(stderr), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var process = new Process { StartInfo = start };
        var clock = Stopwatch.StartNew();
        int? exitCode = null;
        string? error = null;
        bool started = false;
        try
        {
            process.Start();
            started = true;
            process.StandardInput.Close();
            using var stopIo = new CancellationTokenSource();
            var drains = Task.WhenAll(
                process.StandardOutput.BaseStream.CopyToAsync(stdoutFile, stopIo.Token),
                process.StandardError.BaseStream.CopyToAsync(stderrFile, stopIo.Token));
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(), drains).WaitAsync(cancellation);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                error = "Interrupted.";
                try
                {
                    Stop(process);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (Exception exception) when (exception is TimeoutException or Win32Exception)
                {
                    error = "Interrupted, and process exit could not be confirmed: " + exception.Message;
                }
                await stopIo.CancelAsync();
                try { await drains.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or IOException) { }
                throw;
            }
        }
        catch (Win32Exception exception)
        {
            error = exception.Message;
        }
        catch (IOException exception)
        {
            error = exception.Message;
            if (started)
                Stop(process);
        }
        finally
        {
            steps.Add(new(command.Name, command.Executable, command.Arguments, exitCode, clock.ElapsedMilliseconds,
                stdout, stderr, error));
        }
        return steps[^1];
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited while cancellation was being delivered.
        }
    }
}
