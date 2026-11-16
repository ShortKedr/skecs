using System.Text;
using Android.App;
using Android.Util;
using Android.Widget;
using SKECS.GA;
using SKECS.GA.Backend.Vulkan;

[Activity(Name = "org.skecs.ga.movement.MainActivity", Label = "SKECS GA Movement",
    MainLauncher = true, Exported = true)]
public sealed class MovementActivity : Activity
{
    private const string Tag = "SkecsGaMovement";
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _paused = true;
    private int _admittedTicks;
    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _status = new TextView(this) { Text = "Running Movement conformance" };
        SetContentView(_status);
        string mode = Intent?.GetStringExtra("mode") ?? "auto";
        string profile = Intent?.GetStringExtra("profile") ?? "short";
        var cancellation = _lifetime.Token;
        _ = Task.Run(() => Run(mode, profile, cancellation));
    }

    protected override void OnResume()
    {
        base.OnResume();
        lock (_lifecycle)
        {
            _paused = false;
            Report($"lifecycle=resume admittedTicks={_admittedTicks}");
            _resume.TrySetResult();
        }
    }

    protected override void OnPause()
    {
        lock (_lifecycle)
        {
            if (!_paused)
            {
                _paused = true;
                _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            Report($"lifecycle=pause admittedTicks={_admittedTicks}");
        }
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDestroy();
    }

    private async Task Run(string mode, string profile, CancellationToken cancellation)
    {
        try
        {
            var policy = mode switch
            {
                "auto" or "fallback" => GaPolicy.Auto,
                "cpu" => GaPolicy.CpuOnly,
                "gpu" => GaPolicy.RequireGpu,
                _ => throw new ArgumentException($"Unknown Movement mode '{mode}'.")
            };
            var contract = MovementFixture.CreateContract();
            var artifact = GaArtifactCandidate.Load(ReadAsset("movement.manifest.json"),
                Encoding.UTF8.GetString(ReadAsset("movement.content-id")).Trim(),
                ReadAsset("movement.spv"), ReadAsset("movement.layout"), ReadAsset("movement.source-map.json"));
            if (artifact.Bundle is null)
                throw new InvalidDataException(artifact.Error);

            GaVulkanBackend? backend = null;
            if (policy != GaPolicy.CpuOnly)
            {
                string? nativeDirectory = ApplicationInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(nativeDirectory))
                    Report("vulkanUnavailable=Android reported no native library directory");
                else
                {
                    var library = Path.Combine(nativeDirectory,
                        mode == "fallback" ? "libga-vulkan-unavailable.so" : "libga-vulkan-native.so");
                    if (!GaVulkanBackend.TryOpen(library, out backend, out string? error))
                        Report($"vulkanUnavailable={error}");
                }
            }
            using (backend)
            using (var startup = GaStartup.Select(policy, [new(contract, [artifact])], backend?.Devices ?? [],
                allowExperimental: true))
            {
                var decision = startup.Decisions.Single();
                if (!startup.Succeeded || mode == "fallback" && decision.Reason != GaStartupReason.NoComputeApi)
                    throw new InvalidOperationException($"Route {decision.Route}, {decision.Reason}: {decision.Detail}");
                Report($"mode={mode} route={decision.Route} reason={decision.Reason} device={decision.Device} " +
                    $"manifest={artifact.Bundle.ContentId} abi={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
                var gpu = decision.Route == GaRoute.Gpu ? startup : null;
                await MovementFixture.Run(profile, $"Android {mode} {decision.Route}", gpu, contract, Report,
                    BeforeTick, cancellation);
                Report($"result=passed mode={mode} route={decision.Route} profile={profile}");
                SetStatus($"Passed — {decision.Route} ({profile})", cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Report($"result=cancelled mode={mode} profile={profile}");
        }
        catch (Exception failure)
        {
            Log.Error(Tag, $"result=failed mode={mode} profile={profile} {failure}");
            SetStatus($"Failed — {failure.Message}", cancellation);
        }
    }

    private async ValueTask BeforeTick(CancellationToken cancellation)
    {
        while (true)
        {
            Task resumed;
            lock (_lifecycle)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!_paused)
                {
                    _admittedTicks++;
                    return;
                }
                resumed = _resume.Task;
            }
            await resumed.WaitAsync(cancellation);
        }
    }

    private byte[] ReadAsset(string name)
    {
        using var input = Assets!.Open("bundle/" + name);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static void Report(string message) => Log.Info(Tag, message);

    private void SetStatus(string text, CancellationToken cancellation) => RunOnUiThread(() =>
    {
        if (!cancellation.IsCancellationRequested && _status is not null)
            _status.Text = text;
    });
}
