#nullable enable
using System;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.GA.Unity;
using UnityEngine;
using static HostLifecycleChecks;

internal static class GaUnityLifecycleConformance
{
    internal static async Task Run(bool useGpu)
    {
        var asset = UnityHostLifecycle.LoadAsset();
        Require(asset, "The Unity HostLifecycle GA asset is missing.");
        var request = UnityHostLifecycle.CreateRequest(asset);
        var requests = new[]
        {
            request,
            new GaSystemRequest(request.Contract with { SystemId = "host-observer" },
                Array.Empty<GaArtifactCandidate>(), DeclaredCpuStage: true)
        };
        var devices = GaUnityDevice.Discover(new[] { asset });
        using var cpu = await GaStartup.SelectAsync(GaPolicy.CpuOnly, requests, devices);
        using var target = await GaStartup.SelectAsync(useGpu ? GaPolicy.RequireGpu : GaPolicy.Auto,
            requests, devices, allowExperimental: true);
        Require(cpu.SelectedPlan == GaSelectedPlan.CpuOnly &&
            target.SelectedPlan == (useGpu ? GaSelectedPlan.Mixed : GaSelectedPlan.CpuOnly) &&
            target.Decisions[0].Route == (useGpu ? GaRoute.Gpu : GaRoute.Cpu) &&
            target.Decisions[1].Reason == GaStartupReason.DeclaredCpuStage,
            "Unity HostLifecycle selected an unexpected plan.");

        var expected = await Exercise(() => Plan(cpu), "unity-cpu", 127, report: Debug.Log);
        int rows = 0;
        int submissions = 0;
        int drains = 0;
        GaExecutionException actual;
        if (useGpu)
        {
            var contract = request.Contract;
            var kernel = (GaPackedKernel)target.GetPreparedKernel(contract.SystemId);
            var processor = new GaBatchProcessor<HostInput>(target, contract.SystemId, contract.SourceDigest,
                contract.IrDigest, contract.ComponentSchemaDigest, UnityHostLifecycleRows.WordsPerRow,
                UnityHostLifecycleRows.Pack<HostInput>, UnityHostLifecycleRows.Apply<HostInput>, contract.EventCapacity,
                timing => rows += timing.Rows, null, false, Array.Empty<GaComponentRemoval>(),
                Array.Empty<GaComponentAddition>(), UnityHostLifecycleRows.TickResource);
            var batches = new AuditedBatches(processor);
            actual = await Exercise(() => WithObserver(new DeterministicWorkload<HostInput>()
                .AddBatchSystem(SystemStage.Update, UnityHostLifecycleRows.CreateQuery(), batches)),
                "unity-mixed", 64, batches, kernel, report: Debug.Log);
            submissions = batches.Submitted;
            drains = batches.Discarded;
            Require(rows == 4 * HostSession.RowCount + 64 && submissions == 16 && drains == 3 && batches.InFlight == 0,
                "Unity HostLifecycle device rows, submissions, or drains differ.");
        }
        else
        {
            actual = await Exercise(() => Plan(target), "unity-auto-cpu", 64, report: Debug.Log);
        }

        Require(expected.TickIndex == actual.TickIndex && expected.Stage == actual.Stage &&
            expected.SystemOrder == actual.SystemOrder && expected.KernelOrder == actual.KernelOrder &&
            expected.SourceOrderOrdinal == actual.SourceOrderOrdinal && expected.SourceId == actual.SourceId &&
            expected.SourceGeneration == actual.SourceGeneration && expected.KernelFault == actual.KernelFault,
            "Unity HostLifecycle CPU and target faults differ.");
        Debug.Log($"GA_UNITY_SG09_OK deviceRows={rows} submissions={submissions} drains={drains} " +
            "pauseResume=passed pendingReset=passed faultReset=passed discard=passed immutableRender=passed");
    }

    private static DeterministicWorkload<HostInput> Plan(GaStartupResult startup) =>
        WithObserver(UnityHostLifecycle.Register(new DeterministicWorkload<HostInput>(), SystemStage.Update, startup));
}
