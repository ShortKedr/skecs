using System.Runtime.InteropServices.JavaScript;
using SpaceBattle.Web.Ga;
using Battle = SpaceBattle.Simulations.Skecs.Battle;

namespace SpaceBattle.Web;

public static partial class BattleBridge
{
    private sealed record PreparedGaTick(Battle Battle, int Tick, Dictionary<EntityKey, bool>? Expired);

    private static ParticleExpiryGpu? gaGpu;
    private static Task gaDisposal = Task.CompletedTask;
    private static PreparedGaTick? gaPrepared;
    private static bool gaPreparing;
    private static string gaStatus = "off";
    private static long gaDeviceRows;

    [JSExport]
    public static string GaStatus() => gaStatus;

    [JSExport]
    public static double GaDeviceRows() => gaDeviceRows;

    [JSExport]
    public static async Task PrepareGaTick(string moduleUrl)
    {
        if (gaPreparing || gaPrepared != null)
            throw new InvalidOperationException("A GA tick is already in progress.");

        var current = Current;
        int tick = unchecked(current.Tick + 1);
        gaPreparing = true;
        try
        {
            if (gaStatus == "off")
            {
                gaStatus = "initializing";
                await gaDisposal;
                if (battle != current) return;

                var opened = await ParticleExpiryGpu.TryOpenAsync(moduleUrl);
                if (battle != current)
                {
                    if (opened != null) await opened.DisposeAsync();
                    return;
                }
                gaGpu = opened;
                gaStatus = opened == null ? "cpu" : "webgpu";
            }

            Dictionary<EntityKey, bool>? expired = null;
            if (gaGpu is { } gpu)
            {
                var rows = new List<Battle.ParticleExpiryRow>();
                current.CopyParticleExpiryRows(rows);
                if (rows.Count != 0)
                {
                    var expirations = new int[rows.Count];
                    for (int i = 0; i < rows.Count; i++) expirations[i] = rows[i].ExpiresTick;

                    try
                    {
                        var flags = await gpu.ClassifyAsync(tick, expirations);
                        expired = new(rows.Count);
                        for (int i = 0; i < rows.Count; i++) expired.Add(rows[i].Entity, flags[i]);
                    }
                    catch (Exception failure) when (failure is JSException or InvalidDataException)
                    {
                        if (battle != current) return;
                        gaStatus = "faulted";
                        ReleaseGaGpu();
                        Console.WriteLine($"WebGPU particle expiry faulted at tick {tick}: {failure.Message}");
                        return;
                    }
                }
            }

            // Readback never mutates the live world. Pausing can hold this result until commit.
            if (battle == current && unchecked(current.Tick + 1) == tick)
                gaPrepared = new(current, tick, expired);
        }
        finally
        {
            gaPreparing = false;
        }
    }

    [JSExport]
    public static int CommitGaTick()
    {
        var prepared = gaPrepared ?? throw new InvalidOperationException("Prepare a GA tick before committing it.");
        gaPrepared = null;
        var current = Current;
        if (prepared.Battle != current || prepared.Tick != unchecked(current.Tick + 1))
            throw new InvalidOperationException("The prepared GA tick is stale.");

        if (prepared.Expired is { } expired)
        {
            current.StepWithParticleExpiry(prepared.Tick, expired);
            gaDeviceRows += expired.Count;
        }
        else
            current.Step();
        statistics.RecordTick(current.LastTickMs, current.LastStageMs);
        return Snapshot();
    }

    private static void ResetGa()
    {
        gaPrepared = null;
        gaStatus = "off";
        gaDeviceRows = 0;
        ReleaseGaGpu();
    }

    private static void ReleaseGaGpu()
    {
        var gpu = gaGpu;
        gaGpu = null;
        if (gpu != null) gaDisposal = ReleaseGaGpuAsync(gpu);
    }

    private static async Task ReleaseGaGpuAsync(ParticleExpiryGpu gpu)
    {
        try { await gpu.DisposeAsync(); }
        catch (JSException) { /* A lost WebGPU device may already have released the JS resources. */ }
    }
}
