using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SKECS.GA;
using SKECS.GA.Backend.Metal;

internal sealed class MovementSession(string bundleDirectory, string nativeLibraryPath, Action<string> report)
{
    private readonly object _gate = new();
    private TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _paused = true;
    private int _admittedTicks;

    internal void Pause()
    {
        lock (_gate)
        {
            if (_paused)
                return;
            _paused = true;
            _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            report($"lifecycle=pause admittedTicks={_admittedTicks}");
        }
    }

    internal void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
                return;
            _paused = false;
            report($"lifecycle=resume admittedTicks={_admittedTicks}");
            _resumed.TrySetResult();
        }
    }

    internal async Task RunAsync(string mode, string profile, CancellationToken cancellation)
    {
        var policy = mode switch
        {
            "auto" or "fallback" => GaPolicy.Auto,
            "cpu" => GaPolicy.CpuOnly,
            "gpu" => GaPolicy.RequireGpu,
            _ => throw new ArgumentException($"Unknown Movement mode '{mode}'.", nameof(mode)),
        };
        if (profile is not ("boundaries" or "short" or "long"))
            throw new ArgumentException($"Unknown Movement profile '{profile}'.", nameof(profile));

        await WaitUntilActive(cancellation, admitTick: false);
        if (OperatingSystem.IsIOS() && RuntimeFeature.IsDynamicCodeSupported)
            throw new InvalidOperationException("The iOS conformance build requires AOT without dynamic code.");

        var contract = MovementFixture.CreateContract();
        IReadOnlyList<GaArtifactCandidate> candidates = [];
        GaMetalBackend? backend = null;
        if (policy != GaPolicy.CpuOnly)
        {
            var candidate = ReadArtifact();
            if (candidate is not null)
            {
                candidates = [candidate];
                if (candidate.Bundle?.Manifest.Contract == contract)
                {
                    string library = mode == "fallback" ? nativeLibraryPath + ".unavailable" : nativeLibraryPath;
                    if (!GaMetalBackend.TryOpen(library, out backend, out var error))
                        report($"metalUnavailable={error}");
                }
            }
        }

        using (backend)
        using (var startup = GaStartup.Select(policy, [new(contract, candidates)], backend?.Devices ?? [],
            allowExperimental: true))
        {
            var decision = startup.Decisions.Single();
            if (!startup.Succeeded || mode == "fallback" && decision.Reason != GaStartupReason.NoComputeApi)
                throw new InvalidOperationException($"Route {decision.Route} ({decision.Reason}). {decision.Detail}");

            report($"mode={mode} route={decision.Route} reason={decision.Reason} device={decision.Device} " +
                $"artifact={decision.ArtifactId} abi={RuntimeInformation.ProcessArchitecture} " +
                $"dynamicCode={RuntimeFeature.IsDynamicCodeSupported}");
            var gpu = decision.Route == GaRoute.Gpu ? startup : null;
            await MovementFixture.Run(profile, $"iOS {mode} {decision.Route}", gpu, contract, report,
                token => WaitUntilActive(token, admitTick: true), cancellation);
            cancellation.ThrowIfCancellationRequested();
            report($"result=passed mode={mode} route={decision.Route} profile={profile}");
        }
    }

    private GaArtifactCandidate? ReadArtifact()
    {
        string stem = Path.Combine(bundleDirectory, "movement");
        try
        {
            return GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
                File.ReadAllText(stem + ".content-id").Trim(), File.ReadAllBytes(stem + ".metallib"),
                File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            report($"metalArtifactUnavailable={error.Message}");
            return null;
        }
    }

    private async ValueTask WaitUntilActive(CancellationToken cancellation, bool admitTick)
    {
        while (true)
        {
            Task resumed;
            lock (_gate)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!_paused)
                {
                    if (admitTick)
                        _admittedTicks++;
                    return;
                }
                resumed = _resumed.Task;
            }
            await resumed.WaitAsync(cancellation);
        }
    }
}
