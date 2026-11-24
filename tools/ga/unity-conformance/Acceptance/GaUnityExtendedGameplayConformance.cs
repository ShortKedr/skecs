using System;
using System.Threading.Tasks;
using CarrierCapacityFixture;
using MultiHitFixture;
using SKECS;
using SKECS.GA;
using UnityEngine;

internal static class GaUnityExtendedGameplayConformance
{
    internal static async Task Run(GaStartupResult cpu, GaStartupResult target)
    {
        bool useGpu = target.SelectedPlan == GaSelectedPlan.Gpu;
        bool singleWorker = Application.platform == RuntimePlatform.WebGLPlayer;
        await RunThermalInventory(cpu, target, useGpu, singleWorker);
        await MultiHitScenario.RunSuite("GA_UNITY_SG05_OK", "matrix", singleWorker, useGpu,
            (group, observe) => UnityMultiHit.Register(group, cpu, observe: observe),
            (group, observe) => UnityMultiHit.Register(group, target, observe: observe),
            DrainDamage, Debug.Log);
        await CarrierChecks.Run("boundaries", "GA_UNITY_SG06_OK", singleWorker, useGpu, requireYield: useGpu,
            (group, observe) => UnityCarrierCapacity.Register(group, cpu, observe: observe),
            (group, observe) => UnityCarrierCapacity.Register(group, target, observe: observe),
            Drain, Debug.Log);
    }

    private static async Task RunThermalInventory(GaStartupResult cpu, GaStartupResult target, bool useGpu, bool singleWorker)
    {
        const string profile = "matrix";
        var (cases, ticks) = ThermalScenario.SelectCases(profile, singleWorker);
        var cpuCoverage = default(ThermalCoverage);
        var targetCoverage = default(ThermalCoverage);
        foreach (var scenario in cases)
        {
            cpuCoverage += await ThermalScenario.Run(scenario, ticks, useGpu: false, requireYield: false,
                (workload, reduction, observe) => UnityThermalInventory.Register(workload, SystemStage.Update, cpu, reduction, observe: observe),
                Drain, Debug.Log);
            targetCoverage += await ThermalScenario.Run(scenario, ticks, useGpu, requireYield: useGpu,
                (workload, reduction, observe) => UnityThermalInventory.Register(workload, SystemStage.Update, target, reduction, observe: observe),
                Drain, Debug.Log);
        }
        ThermalScenario.VerifyCoverage(cpuCoverage, useGpu: false);
        ThermalScenario.VerifyCoverage(targetCoverage, useGpu);
        Debug.Log($"GA_UNITY_SG03_OK profile={profile} cases={cases.Length} ticks={cases.Length * ticks} " +
            $"rows={targetCoverage.Rows} transfers={targetCoverage.Transfers} overheated={targetCoverage.Overheated} " +
            $"heatRefusals={targetCoverage.HeatRefusals} heatSpentRaw={targetCoverage.HeatSpent} docked={targetCoverage.Docked} " +
            $"kits={targetCoverage.Kits} enabledKits={targetCoverage.EnabledKits} slots=0x{targetCoverage.Slots:x4} " +
            $"deviceRows={targetCoverage.DeviceRows} deviceBatches={targetCoverage.DeviceBatches} " +
            "exactState=passed exactTypedEvents=passed typedReduction=passed");
    }

    private static async ValueTask<bool> Drain<T>(DeterministicRunner<T> runner, int budget) where T : unmanaged
    {
        var result = await GaUnityConformance.Advance(runner, budget);
        if (result.Status != DeterministicTickStatus.Completed)
            throw new InvalidOperationException($"Unity fixture tick for {typeof(T).Name} did not complete.", runner.Fault);
        return result.Yielded;
    }

    private static async ValueTask<DeterministicTickStatus> DrainDamage(
        DeterministicRunner<DamageFrame> runner, int budget, bool useGpu) => (await GaUnityConformance.Advance(runner, budget)).Status;
}
