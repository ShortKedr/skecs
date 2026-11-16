using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Skecs.Agentic;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var (code, json) = await ExecuteAsync(args, Environment.CurrentDirectory, cancellation.Token);
            Console.WriteLine(json);
            return code;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static async Task<(int ExitCode, string Json)> ExecuteAsync(string[] args, string cwd, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var started = DateTimeOffset.UtcNow;
        Request? request = null;
        LocalWorkspace? workspace = null;
        string? output = null;
        string? error = null;
        string status;
        SourceIdentity? before = null;
        SourceIdentity? after = null;
        ReplayIdentity[] inputs = [];
        ReplayIdentity[] inputsAfter = [];
        var tools = new Dictionary<string, string>();
        var steps = new List<Step>();
        var artifacts = new List<string>();
        using var timeout = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellation);
        try
        {
            if (args is ["help"] or ["--help"] or [])
                return (0, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    usage = new[] { "list", "run <operation> [arguments] [--configuration Debug|Release] [--timeout-seconds N]" }
                }, Json));

            workspace = LocalWorkspace.Find(cwd);
            if (args is ["list"])
            {
                return (0, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    operations = Operation.Registry.Select(operation =>
                    {
                        string? reason = operation.Missing(workspace);
                        return new { operation.Id, operation.Usage, operation.Project, available = reason is null, reason };
                    }).ToArray()
                }, Json));
            }
            if (args[0] != "run")
                throw new ArgumentException("Expected list or run <operation>.");
            request = Request.Parse(args[1..]);
            var operation = Operation.Registry.SingleOrDefault(candidate => candidate.Id == request.Operation)
                ?? throw new ArgumentException("Unknown operation: " + request.Operation);
            operation.Validate(request);
            error = operation.Missing(workspace);
            if (error is not null)
                status = "unavailable";
            else
            {
                string runDirectory = LocalWorkspace.Output + "/" + Guid.NewGuid().ToString("N");
                string? corpus = operation.Id == "profile-verify" ? workspace.CorpusInput(request.Arguments[0]) : null;
                Command[] commands = operation.Commands(request, workspace, runDirectory);
                Directory.CreateDirectory(workspace.PathFor(runDirectory));
                output = runDirectory;
                timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
                var environment = workspace.EnvironmentForTools();
                var runner = new ProcessRunner(workspace, output, environment, steps);
                if (corpus is not null)
                {
                    await using var input = File.OpenRead(corpus);
                    await using var snapshot = File.Open(workspace.PathFor(output + "/profile-corpus.json"), FileMode.CreateNew);
                    await input.CopyToAsync(snapshot, stop.Token);
                }

                await ReadVersion("git", [.. LocalWorkspace.GitOptions, "--version"]);
                using var buildLock = operation.Project is null ? null : await workspace.LockBuildAsync(stop.Token);
                if (operation.Project is not null)
                    await ReadVersion("dotnet", ["--version"]);
                before = await workspace.SourceAsync(runner, output, "", stop.Token);
                if (operation.Id == "determinism-compare")
                    inputs = await CaptureReplayInputs();
                environment["SKECS_REPLAY_COMMIT"] = before.Head + "+" + before.WorktreeSha256;
                if (tools.TryGetValue("dotnet", out var sdk))
                    environment["SKECS_REPLAY_SDK"] = sdk;

                foreach (var command in commands)
                {
                    var step = await runner.ExecuteAsync(command, stop.Token);
                    if (step.ExitCode != 0)
                    {
                        error = step.Error ?? $"{step.Name} exited with code {step.ExitCode}. See {step.Stdout} and {step.Stderr}.";
                        break;
                    }
                }

                var nativeOutputs = operation.Id switch
                {
                    "diff" => new[] { "diff.stdout.log" },
                    "test" or "ga-test" or "network-test" => ["tests.trx"],
                    "benchmark-list" => ["discovery.stdout.log"],
                    "determinism-run" => ["replay/manifest.json"],
                    "profile" when request.Arguments[1] == "on" => ["profile.json", "profile.xml"],
                    "profile" => ["profile.json"],
                    "profile-verify" => ["profile-corpus.json", "profile-run/proposal.json", "profile-run/verification.json", "profile-run/report.md"],
                    "network-check" => ["replication.stdout.log"],
                    _ => []
                };
                var missing = new List<string>();
                foreach (string artifact in nativeOutputs)
                {
                    string path = output + "/" + artifact;
                    if (File.Exists(workspace.PathFor(path)))
                        artifacts.Add(path);
                    else
                        missing.Add(path);
                }
                if (error is null && missing.Count != 0)
                    error = "Missing expected output: " + string.Join(", ", missing);
                if (operation.Id == "determinism-compare")
                    inputsAfter = await CaptureReplayInputs();
                after = await workspace.SourceAsync(runner, output, "-after", stop.Token);
                if (error is not null)
                    status = "failed";
                else if (!SameSource(before, after))
                {
                    status = "sourceChanged";
                    error = "HEAD or working-tree files changed during verification.";
                }
                else if (!inputs.SequenceEqual(inputsAfter))
                {
                    status = "inputsChanged";
                    error = "Replay input files changed during comparison.";
                }
                else
                    status = "passed";

                async Task<ReplayIdentity[]> CaptureReplayInputs() =>
                [
                    await workspace.ReplayIdentityAsync(request.Arguments[0], stop.Token),
                    await workspace.ReplayIdentityAsync(request.Arguments[1], stop.Token)
                ];

                async Task ReadVersion(string executable, string[] arguments)
                {
                    var step = await runner.ExecuteAsync(new(executable + "-version", executable, arguments), stop.Token);
                    if (step.ExitCode != 0)
                        throw new InvalidOperationException(step.Error ?? $"Cannot read {executable} version. See {step.Stderr}.");
                    tools.Add(executable, (await File.ReadAllTextAsync(workspace.PathFor(step.Stdout), stop.Token)).Trim());
                }
            }
        }
        catch (OperationCanceledException)
        {
            status = cancellation.IsCancellationRequested ? "cancelled" : "timedOut";
            error = status == "cancelled" ? "Run cancelled." : "Run exceeded its time limit.";
        }
        catch (ArgumentException exception)
        {
            status = output is null ? "invalidRequest" : "failed";
            error = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            status = "failed";
            error = exception.Message;
        }

        string? resultFile = output is null ? null : output + "/result.json";
        string Encode() => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status,
            error,
            request,
            startedUtc = started,
            elapsedMilliseconds = clock.ElapsedMilliseconds,
            host = new { runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString() },
            tools,
            source = before,
            sourceAfter = after,
            sourceUnchanged = after is null ? (bool?)null : SameSource(before!, after),
            inputs,
            inputsAfter,
            resultFile,
            steps,
            artifacts
        }, Json);

        string json = Encode();
        if (resultFile is not null)
        {
            try
            {
                string temporary = workspace!.PathFor(output + "/result.tmp");
                await File.WriteAllTextAsync(temporary, json + "\n", CancellationToken.None);
                File.Move(temporary, workspace.PathFor(resultFile));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                status = "failed";
                error = string.Join(" ", new[] { error, "Cannot persist result: " + exception.Message }.OfType<string>());
                resultFile = null;
                json = Encode();
            }
        }

        int exitCode = status switch
        {
            "passed" => 0,
            "invalidRequest" => 2,
            "unavailable" => 3,
            "timedOut" => 124,
            "cancelled" => 130,
            _ => 1
        };
        return (exitCode, json);
    }

    private static bool SameSource(SourceIdentity before, SourceIdentity after) =>
        before.Head == after.Head && before.WorktreeSha256 == after.WorktreeSha256;
}
