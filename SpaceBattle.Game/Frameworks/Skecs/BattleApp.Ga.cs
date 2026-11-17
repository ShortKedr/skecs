using System.Runtime.InteropServices;
using System.Text.Json;
using SKECS.GA;
using SpaceBattle;

namespace SpaceBattle.Game.Skecs;

internal sealed partial class BattleApp
{
    private string? gaBackend, gaBridge, gaBundle;
    private int gaDeviceIndex;
    private GaPolicy gaPolicy;
    private NativeParticleExpiry? particleExpiry;
    private Exception? gaFault;
    private bool restartPending;
    private int? deferredWatch;
    private TeamPopulation publishedPopulation;
    private string publishedFramework = "";
    private (int Alive, int Ships, int Turrets, int Projectiles, int Debris, int Particles, int Bodies, int Fixtures) publishedCounts;

    private int gaValidationTicks;
    private bool validationAwaitingRender;
    private Exception? gaValidationError;
    private bool gaValidateFault, faultArmed, validationRestartRequested;
    private int? pauseTick;
    private int pauseFrames, pendingFrames, frozenFrames;
    private long validationRows;
    private readonly List<string> validationHashes = [];

    private bool WorldLocked => particleExpiry?.HasPending == true || gaFault is not null;

    private void ConfigureGa(string[] args, Func<string, string?> value, Func<string, int, int> number)
    {
        gaBackend = value("--ga-backend");
        gaValidationTicks = number("--ga-validate-ticks", 0);
        gaValidateFault = args.Contains("--ga-validate-fault");
        if (args.Contains("--ga-validate-ticks") && gaValidationTicks is < 1 or > 10_000)
            throw new ArgumentException("--ga-validate-ticks requires a count from 1 to 10000.");
        if (gaValidationTicks > 0 && (capture is not null || validateControls || validateLiveStatistics ||
            validateEffects || validateGunTints || validateSceneStyle || profileFrames > 0))
            throw new ArgumentException("Run GA live validation separately from capture, profiling and other validation.");

        gaBridge = value("--ga-bridge");
        gaBundle = value("--ga-bundle");
        gaDeviceIndex = number("--ga-device", 0);
        string? policy = value("--ga-policy");
        if (gaBackend is null)
        {
            if (gaBridge is not null || gaBundle is not null || args.Contains("--ga-device") || policy is not null || gaValidateFault)
                throw new ArgumentException("Native GA options require --ga-backend vulkan or d3d12.");
            return;
        }

        if (gaBackend is not ("vulkan" or "d3d12") || string.IsNullOrWhiteSpace(gaBridge) ||
            string.IsNullOrWhiteSpace(gaBundle) || gaDeviceIndex < 0)
            throw new ArgumentException("Specify --ga-backend vulkan or d3d12, --ga-bridge PATH, --ga-bundle DIR and a nonnegative device index.");
        gaBridge = Path.GetFullPath(gaBridge);
        gaBundle = Path.GetFullPath(gaBundle);
        gaPolicy = policy switch
        {
            null or "require-gpu" => GaPolicy.RequireGpu,
            "auto" => GaPolicy.Auto,
            _ => throw new ArgumentException("--ga-policy must be require-gpu or auto."),
        };
        if (profileFrames > 0 || validateLiveStatistics)
            throw new ArgumentException("Use ordinary desktop execution or --ga-validate-ticks with native GA.");
        if (gaValidateFault && (gaValidationTicks == 0 || gaBackend != "d3d12" || gaPolicy != GaPolicy.RequireGpu))
            throw new ArgumentException("GA fault validation requires --ga-validate-ticks and a D3D12 test bridge.");
    }

    private void OpenGa()
    {
        if (gaBackend is null) return;
        particleExpiry = NativeParticleExpiry.Open(gaBackend, gaBridge!, gaBundle!, gaDeviceIndex, gaPolicy);
        var decision = particleExpiry.Decision;
        Console.WriteLine($"Particle expiry GA {decision.Route} ({decision.Reason}) {decision.Device?.Device} {decision.ArtifactId}");
    }

    private void UpdateGpu(double dt)
    {
        var ga = particleExpiry!;
        double tickDuration = 1.0 / config.TickRate;
        accumulator = Math.Min(accumulator + Math.Min(dt, .25) * speed, 32 * tickDuration);
        try
        {
            if (ga.HasPending)
            {
                if (!ga.Complete()) return;
            }
            else
            {
                if (accumulator < tickDuration) return;
                accumulator -= tickDuration;
                if (ga.TryBegin(battle)) return;
            }
        }
        catch (Exception error)
        {
            StopOnGaFault(error);
            return;
        }

        if (deferredWatch is { } serial)
        {
            deferredWatch = null;
            if (serial < 0) StopFollow();
            else StartFollow(serial);
        }
        ResolveObserver();
        statistics.RecordTick(battle.LastTickMs, battle.LastStageMs);
        wallTicks++;
        battle.Capture(frame, refreshEntities: false);
        RefreshPresentation();
        if (gaValidationTicks > 0) RecordValidationTick();
    }

    private void CompleteGaRestart()
    {
        try
        {
            if (!particleExpiry!.TryDiscard()) return;
        }
        catch (Exception error)
        {
            StopOnGaFault(error);
            return;
        }

        restartPending = false;
        deferredWatch = null;
        Restart();
    }

    private void StopOnGaFault(Exception error)
    {
        gaFault = error;
        paused = true;
        ResetRates();
        Console.Error.WriteLine(error);
    }

    private void RefreshPresentation()
    {
        publishedPopulation = new TeamPopulation { Cyan = battle.TeamShipCount(0), Red = battle.TeamShipCount(1) };
        publishedFramework = battle.Framework;
        publishedCounts = (AliveEntities, battle.ShipCount, battle.TurretCount, battle.ProjectileCount,
            battle.DebrisCount, battle.ParticleCount, battle.PhysicsBodies, battle.PhysicsFixtures);
        CaptureColliders();
    }

    private void RecordValidationTick()
    {
        if (battle.Tick <= gaValidationTicks)
        {
            if (battle.Tick != validationHashes.Count)
                throw new InvalidOperationException("GA validation skipped a simulation tick.");
            validationHashes.Add(battle.Checksum().ToString("X16"));
            validationRows += particleExpiry?.LastRows ?? 0;
        }
    }

    private void ValidateRenderedFrame()
    {
        validationAwaitingRender = false;
        bool pending = particleExpiry?.HasPending == true;
        if (pending) pendingFrames++;
        if (gaFault is not null)
        {
            if (!gaValidateFault || !faultArmed || frame.Tick != validationHashes.Count - 1 ||
                !gaFault.ToString().Contains("0x887A0005", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected error in desktop GA validation.", gaFault);
            if (++frozenFrames == 3) FinishGaValidation();
            return;
        }

        bool gpu = particleExpiry?.Decision.Route == GaRoute.Gpu;
        if (pauseTick is null && (gpu ? pending : frame.Tick > 0))
        {
            pauseTick = frame.Tick;
            TogglePause();
        }
        if (paused && pauseTick is { } tick)
        {
            if (frame.Tick != tick) throw new InvalidOperationException("The published frame advanced while paused.");
            if (++pauseFrames == 3) TogglePause();
            return;
        }

        if (gaValidateFault && !faultArmed && validationRows > 0 && !pending)
        {
            var library = NativeLibrary.Load(gaBridge!);
            try
            {
                var arm = Marshal.GetDelegateForFunctionPointer<ArmDeviceRemoval>(
                    NativeLibrary.GetExport(library, "skecs_ga_d3d12_test_arm_removal"));
                arm();
                faultArmed = true;
            }
            finally { NativeLibrary.Free(library); }
        }

        if (validationRestartRequested && !restartPending)
        {
            if (frame.Tick != 0 || !validationHashes[0].Equals(battle.Checksum().ToString("X16"), StringComparison.Ordinal))
                throw new InvalidOperationException("The published restart frame did not match the initial state.");
            FinishGaValidation();
        }
        else if (!validationRestartRequested && !gaValidateFault && frame.Tick >= gaValidationTicks && (!gpu || pending))
        {
            validationRestartRequested = true;
            Restart();
        }
        else if (gaValidateFault && frame.Tick >= gaValidationTicks)
            throw new InvalidOperationException("The instrumented GPU did not report device removal.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ArmDeviceRemoval();

    private void FinishGaValidation()
    {
        var decision = particleExpiry?.Decision;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Route = decision?.Route.ToString() ?? "Cpu",
            Reason = decision?.Reason.ToString() ?? "CpuOnlyPolicy",
            Device = decision?.Device,
            ArtifactId = decision?.ArtifactId,
            Rows = validationRows,
            PauseVerified = pauseFrames == 3,
            RestartVerified = validationRestartRequested,
            PendingFrames = pendingFrames,
            FrozenFrames = frozenFrames,
            FaultTick = gaFault is null ? (int?)null : frame.Tick,
            Hashes = validationHashes,
        }));
        window.Close();
    }
}
