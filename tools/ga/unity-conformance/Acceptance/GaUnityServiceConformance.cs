#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.GA.Unity;
using UnityEngine;

internal static class GaUnityServiceConformance
{
    internal static async Task Run(bool useGpu)
    {
        var asset = UnityServiceContinuation.LoadAsset();
        ContinuationChecks.Require(asset, "The Unity ServiceContinuation GA asset is missing.");
        var integration = UnityServiceContinuation.CreateRequest(asset);
        string digest = GaArtifactManifest.ComputeDigest(Encoding.UTF8.GetBytes(ContactService.Contract));
        var service = new GaContractIdentity("contact-service", ContactService.Contract, digest, digest, digest,
            GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion);
        var requests = new[]
        {
            integration,
            new GaSystemRequest(service, Array.Empty<GaArtifactCandidate>(), DeclaredCpuStage: true)
        };
        var devices = GaUnityDevice.Discover(new[] { asset });
        using var cpu = await GaStartup.SelectAsync(GaPolicy.CpuOnly, requests, devices);
        using var target = await GaStartup.SelectAsync(useGpu ? GaPolicy.RequireGpu : GaPolicy.Auto,
            requests, devices, allowExperimental: true);
        ContinuationChecks.Require(cpu.SelectedPlan == GaSelectedPlan.CpuOnly &&
            target.SelectedPlan == (useGpu ? GaSelectedPlan.Mixed : GaSelectedPlan.CpuOnly) &&
            target.Decisions[0].Route == (useGpu ? GaRoute.Gpu : GaRoute.Cpu) &&
            target.Decisions[1].Route == GaRoute.Cpu &&
            target.Decisions[1].Reason == GaStartupReason.DeclaredCpuStage,
            "Unity ServiceContinuation selected an unexpected integration or ordered service route.");

        long rows = 0;
        bool pending = false;
        var reference = await ContinuationChecks.RunAsync(serviceState => Plan(serviceState, cpu), "unity-cpu", 127,
            report: Debug.Log);
        var actual = await ContinuationChecks.RunAsync(
            serviceState => Plan(serviceState, target, timing => rows += timing.Rows),
            useGpu ? "unity-mixed" : "unity-auto-cpu", 64, () => pending = true, Debug.Log);
        ContinuationChecks.Compare(reference, actual, "Unity CPU and target ServiceContinuation");
        int expected = actual.Sum(frame => frame.Rows)
            + actual.Where(frame => frame.Tick > ContinuationChecks.ForkTick).Sum(frame => frame.Rows)
            + actual[ContinuationChecks.ForkTick].Rows + 2 * actual[actual.Length - 1].Rows;
        ContinuationChecks.Require(rows == (useGpu ? expected : 0) && (!useGpu || pending),
            "Unexpected Unity ServiceContinuation device rows or missing pending readback.");
        Debug.Log($"GA_UNITY_SG08_OK ticks={actual.Length} restoredTicks={actual.Length - ContinuationChecks.ForkTick} " +
            $"deviceRows={rows} exactCheckpoint=passed exactOrderedEvents=passed faultRecovery=passed");
    }

    private static DeterministicWorkload<IntegrationFrame> Plan(ContactService service, GaStartupResult startup,
        Action<GaBatchTimings>? observe = null) => new DeterministicWorkload<IntegrationFrame>()
        .AddCommitGroup(SystemStage.Update, group =>
        {
            UnityServiceContinuation.Register(group, startup, observe: observe);
            group.AddOrderedApply(new DeterministicQuery().Write<ContactMotion>(), service.Apply);
        });
}
