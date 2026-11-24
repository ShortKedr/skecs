using System;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using ShotBurstFixture;
using UnityEngine;

internal static class GaUnityGameplayConformance
{
    internal static async Task Run(GaStartupResult cpu, GaStartupResult target)
    {
        bool useGpu = target.SelectedPlan == GaSelectedPlan.Gpu;
        bool singleWorker = Application.platform == RuntimePlatform.WebGLPlayer;
        const string profile = "matrix";
        await RunTargetRead(cpu, target, useGpu, singleWorker, profile);
        await RunShotBurst(cpu, target, useGpu, singleWorker, profile);
    }

    private static async Task RunTargetRead(
        GaStartupResult cpu, GaStartupResult target, bool useGpu, bool singleWorker, string profile)
    {
        var (cases, ticks) = TargetScenario.SelectCases(profile, singleWorker);
        var cpuCoverage = default(TargetCoverage);
        var targetCoverage = default(TargetCoverage);
        foreach (var scenario in cases)
        {
            cpuCoverage += await TargetScenario.Run(scenario, ticks, useGpu: false, requireYield: false,
                (workload, snapshot, observe) => UnityTargetRead.Register(workload, SystemStage.Update, cpu, snapshot, observe: observe),
                Drain, Debug.Log);
            targetCoverage += await TargetScenario.Run(scenario, ticks, useGpu, requireYield: useGpu,
                (workload, snapshot, observe) => UnityTargetRead.Register(workload, SystemStage.Update, target, snapshot, observe: observe),
                Drain, Debug.Log);
        }
        TargetScenario.VerifyCoverage(cpuCoverage, useGpu: false);
        TargetScenario.VerifyCoverage(targetCoverage, useGpu);
        Debug.Log($"GA_UNITY_SG02_OK profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
            $"rows={targetCoverage.Rows} deviceRows={targetCoverage.DeviceRows} deviceBatches={targetCoverage.DeviceBatches} " +
            "exactState=passed exactTypedEvents=passed frozenPriorPosition=passed");
    }

    private static async Task RunShotBurst(
        GaStartupResult cpu, GaStartupResult target, bool useGpu, bool singleWorker, string profile)
    {
        var (cases, ticks) = ShotBurstScenario.SelectCases(profile, singleWorker);
        var totals = default(Totals);
        long rows = 0;
        long batches = 0;
        foreach (var scenario in cases)
        {
            var result = await ShotBurstScenario.RunCase(scenario, ticks, useGpu,
                group => UnityShotBurst.Register(group, cpu),
                (group, observe) => UnityShotBurst.Register(group, target, observe: observe),
                Drain, Debug.Log);
            totals = totals.With(result.Counters);
            rows += result.DeviceRows;
            batches += result.DeviceBatches;
        }
        ShotBurstScenario.VerifyCoverage(totals);
        Debug.Log($"GA_UNITY_SG04_OK profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
            $"requests={totals.Requests} accepted={totals.Accepted} capacityRejected={totals.Rejected} " +
            $"heatRejected={totals.HeatRejected} zeroReseeds={totals.ZeroReseeds} deviceRows={rows} deviceBatches={batches} " +
            "domainAndOrdinals=passed fixedAndRng=passed ticketIds=passed reusedSourceGeneration=passed");
    }

    private static async ValueTask<bool> Drain(DeterministicRunner<byte> runner, int budget)
    {
        var result = await GaUnityConformance.Advance(runner, budget);
        if (result.Status != DeterministicTickStatus.Completed)
            throw new InvalidOperationException("TargetRead Unity tick did not complete.", runner.Fault);
        return result.Yielded;
    }

    private static async ValueTask Drain(DeterministicRunner<ShotFrame> runner, int budget, bool useGpu)
    {
        var result = await GaUnityConformance.Advance(runner, budget);
        if (result.Status != DeterministicTickStatus.Completed)
            throw new InvalidOperationException("ShotBurst Unity tick did not complete.", runner.Fault);
    }
}
