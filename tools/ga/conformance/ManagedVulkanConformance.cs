using SKECS.GA;
using SKECS.GA.Backend.Vulkan;

internal sealed class ManagedVulkanConformance : IDisposable
{
    private readonly GaStartupResult _startup;
    private readonly GaVulkanKernel _kernel;

    private ManagedVulkanConformance(GaStartupResult startup, GaVulkanKernel kernel)
    {
        _startup = startup;
        _kernel = kernel;
    }

    public static ManagedVulkanConformance Open(string libraryPath, int deviceIndex, GaArtifactBundle bundle)
    {
        using var backend = GaVulkanBackend.Open(libraryPath);
        if ((uint)deviceIndex >= (uint)backend.Devices.Count)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));

        IGaDevice[] devices = [backend.Devices[deviceIndex]];
        var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
        using var cpu = GaStartup.Select(GaPolicy.CpuOnly, [request], devices);
        if (cpu.SelectedPlan != GaSelectedPlan.CpuOnly || cpu.Decisions[0].Reason != GaStartupReason.CpuOnlyPolicy)
            throw new InvalidOperationException("CpuOnly unexpectedly selected a Vulkan kernel.");
        using var defaultStrict = GaStartup.Select(GaPolicy.RequireGpu, [request], devices);
        if (defaultStrict.Succeeded || defaultStrict.Decisions[0].Reason != GaStartupReason.UnqualifiedConfiguration)
            throw new InvalidOperationException("RequireGpu accepted the experimental Vulkan route without opt-in.");

        var startup = GaStartup.Select(GaPolicy.RequireGpu, [request], devices, allowExperimental: true);
        try
        {
            var decision = startup.Decisions[0];
            if (startup.SelectedPlan != GaSelectedPlan.Gpu || decision.Qualification != GaQualification.Experimental ||
                decision.ArtifactId != bundle.ContentId || decision.Device != devices[0].Info ||
                startup.GetPreparedKernel(request.Contract.SystemId) is not GaVulkanKernel kernel)
                throw new InvalidOperationException($"Managed Vulkan startup did not select the requested kernel. {decision}");

            var empty = kernel.BeginDispatch([0], 1).Drain();
            if (!empty.Output.IsEmpty || !empty.Faults.IsEmpty)
                throw new InvalidOperationException("An empty Vulkan payload returned nonempty results.");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                kernel.BeginDispatch([0], 1, canceled.Token);
                throw new InvalidOperationException("A canceled Vulkan payload was accepted.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested)
            {
            }
            return new ManagedVulkanConformance(startup, kernel);
        }
        catch
        {
            startup.Dispose();
            throw;
        }
    }

    public void Compare(
        ReadOnlySpan<uint> input, int rowWidthWords, ReadOnlySpan<uint> expectedOutput, ReadOnlySpan<uint> expectedFaults)
    {
        var captured = input.ToArray();
        var submission = _kernel.BeginDispatch(captured, rowWidthWords);
        Array.Fill(captured, 0xffffffffu);
        var result = submission.TryGetResult(out var ready) ? ready : submission.Drain();
        if (result.Output.Length != expectedOutput.Length || !result.Faults.SequenceEqual(expectedFaults))
            throw new InvalidOperationException("Managed Vulkan output length or complete fault trace diverged.");
        for (int row = 0; row < (int)input[0]; row++)
        {
            if (expectedFaults[row * GaSlangModule.FaultStride] != 0)
                continue;
            var offset = row * rowWidthWords;
            if (!result.Output.Slice(offset, rowWidthWords).SequenceEqual(expectedOutput.Slice(offset, rowWidthWords)))
                throw new InvalidOperationException($"Managed Vulkan diverged on successful row {row}.");
        }
    }

    public void VerifyDiscard(ReadOnlySpan<uint> input, int rowWidthWords)
    {
        var submission = _kernel.BeginDispatch(input, rowWidthWords);
        submission.Discard();
        if (!submission.IsCompleted)
            throw new InvalidOperationException("A discarded GPU submission retained pending work.");
        try
        {
            submission.Drain();
            throw new InvalidOperationException("A discarded GPU submission exposed its result.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => _startup.Dispose();
}
