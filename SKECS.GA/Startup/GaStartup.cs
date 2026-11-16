using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SKECS.GA;

public enum GaPolicy { CpuOnly, Auto, RequireGpu }
public enum GaRoute { Cpu, Gpu, Rejected }
public enum GaSelectedPlan { CpuOnly, Gpu, Mixed, StartupFailure }
public enum GaQualification { NotQualified, Experimental, Qualified }
public enum GaStartupReason
{
    None, CpuOnlyPolicy, DeclaredCpuStage, NoComputeApi, MissingCapability, MissingArtifact,
    IncompatibleManifest, PipelineCreationFailed, ProbeFailed, ResourceBudgetExceeded,
    CompilerCpuOnly, UnqualifiedConfiguration
}

public sealed record GaDeviceInfo(string Backend, string Api, string ApiVersion, string Device, string Driver);

public sealed record GaDeviceProfile(
    string Profile, GaQualification Qualification, IReadOnlyList<string> Features,
    int MaxWorkgroupX, int MaxWorkgroupY, int MaxWorkgroupZ, int MaxWorkgroupInvocations,
    int MaxSharedMemoryBytes, long MaxStorageBytes);

public interface IGaPreparedKernel : IDisposable;

public sealed record GaPreparation(IGaPreparedKernel? Kernel, GaStartupReason Reason, string? Detail = null);

public interface IGaDevice
{
    GaDeviceInfo Info { get; }
    GaArtifactFormat Format { get; }
    IReadOnlyList<GaDeviceProfile> Profiles { get; }

    // Prepare validates target-specific limits and bindings, creates the pipeline and runs its probe.
    // A failure is returned only after releasing any partially created device resources.
    GaPreparation Prepare(GaArtifactBundle artifact);

    ValueTask<GaPreparation> PrepareAsync(GaArtifactBundle artifact) => new(Prepare(artifact));
}

public sealed class GaArtifactCandidate
{
    private GaArtifactCandidate(GaArtifactBundle? bundle, string? error)
    {
        Bundle = bundle;
        Error = error;
    }

    public GaArtifactCandidate(GaArtifactBundle bundle) : this(
        bundle ?? throw new ArgumentNullException(nameof(bundle)), null) { }

    public GaArtifactBundle? Bundle { get; }
    public string? Error { get; }

    public static GaArtifactCandidate Load(
        ReadOnlySpan<byte> manifest, string expectedContentId, ReadOnlySpan<byte> artifact,
        ReadOnlySpan<byte> reflection, ReadOnlySpan<byte> sourceMap)
    {
        try
        {
            return new GaArtifactCandidate(GaArtifactBundle.Load(
                GaArtifactManifest.Parse(manifest), expectedContentId, artifact, reflection, sourceMap));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            return new GaArtifactCandidate(null, error.Message);
        }
    }
}

public sealed record GaSystemRequest(
    GaContractIdentity Contract, IReadOnlyList<GaArtifactCandidate> Artifacts,
    bool DeclaredCpuStage = false, string? CompilerCpuOnlyReason = null);

public sealed record GaStartupDecision(
    GaContractIdentity Contract, GaRoute Route, GaStartupReason Reason, string? Detail = null,
    GaDeviceInfo? Device = null, string? ComputeProfile = null, string? ArtifactId = null,
    GaQualification? Qualification = null);

public sealed class GaStartupResult : IDisposable
{
    private readonly Dictionary<string, IGaPreparedKernel> _kernels;
    private bool _disposed;

    internal GaStartupResult(GaPolicy policy, GaStartupDecision[] decisions, Dictionary<string, IGaPreparedKernel> kernels)
    {
        RequestedPolicy = policy;
        Decisions = Array.AsReadOnly(decisions);
        var hasGpu = decisions.Any(decision => decision.Route == GaRoute.Gpu);
        SelectedPlan = decisions.Any(decision => decision.Route == GaRoute.Rejected) ? GaSelectedPlan.StartupFailure
            : !hasGpu ? GaSelectedPlan.CpuOnly
            : decisions.Any(decision => decision.Route == GaRoute.Cpu) ? GaSelectedPlan.Mixed
            : GaSelectedPlan.Gpu;
        _kernels = kernels;
    }

    public GaPolicy RequestedPolicy { get; }
    public bool Succeeded => SelectedPlan != GaSelectedPlan.StartupFailure;
    public GaSelectedPlan SelectedPlan { get; }
    public IReadOnlyList<GaStartupDecision> Decisions { get; }

    public IGaPreparedKernel GetPreparedKernel(string systemId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Succeeded)
            throw new InvalidOperationException("GA startup failed.");
        return _kernels[systemId];
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GaStartup.DisposeKernels(_kernels);
    }
}

public static class GaStartup
{
    public static GaStartupResult Select(
        GaPolicy policy, IReadOnlyList<GaSystemRequest> systems, IReadOnlyList<IGaDevice> devices,
        bool allowExperimental = false) =>
        SelectCore(policy, systems, devices, allowExperimental, asynchronous: false).GetAwaiter().GetResult();

    public static ValueTask<GaStartupResult> SelectAsync(
        GaPolicy policy, IReadOnlyList<GaSystemRequest> systems, IReadOnlyList<IGaDevice> devices,
        bool allowExperimental = false) =>
        SelectCore(policy, systems, devices, allowExperimental, asynchronous: true);

    private static async ValueTask<GaStartupResult> SelectCore(
        GaPolicy policy, IReadOnlyList<GaSystemRequest> systems, IReadOnlyList<IGaDevice> devices,
        bool allowExperimental, bool asynchronous)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(devices);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var system in systems)
        {
            if (system?.Contract is null || string.IsNullOrWhiteSpace(system.Contract.SystemId) ||
                !ids.Add(system.Contract.SystemId) || system.Artifacts is null)
                throw new ArgumentException("GA requests require unique system IDs and artifact lists.", nameof(systems));
        }

        var kernels = new Dictionary<string, IGaPreparedKernel>(StringComparer.Ordinal);
        var decisions = new GaStartupDecision[systems.Count];
        try
        {
            for (int i = 0; i < systems.Count; i++)
                decisions[i] = await SelectSystem(policy, systems[i], devices, allowExperimental, asynchronous, kernels);
        }
        catch (Exception error)
        {
            try
            {
                DisposeKernels(kernels);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("GA preparation and resource cleanup failed.", error, cleanupError);
            }
            throw;
        }

        if (decisions.Any(decision => decision.Route == GaRoute.Rejected))
            DisposeKernels(kernels);
        return new GaStartupResult(policy, decisions, kernels);
    }

    private static async ValueTask<GaStartupDecision> SelectSystem(
        GaPolicy policy, GaSystemRequest system, IReadOnlyList<IGaDevice> devices, bool allowExperimental,
        bool asynchronous, Dictionary<string, IGaPreparedKernel> kernels)
    {
        if (system.DeclaredCpuStage)
            return new GaStartupDecision(system.Contract, GaRoute.Cpu, GaStartupReason.DeclaredCpuStage);
        if (policy == GaPolicy.CpuOnly)
            return new GaStartupDecision(system.Contract, GaRoute.Cpu, GaStartupReason.CpuOnlyPolicy);

        var fallbackRoute = policy == GaPolicy.RequireGpu ? GaRoute.Rejected : GaRoute.Cpu;
        if (system.CompilerCpuOnlyReason is not null)
            return new GaStartupDecision(
                system.Contract, fallbackRoute, GaStartupReason.CompilerCpuOnly, system.CompilerCpuOnlyReason);
        if (system.Artifacts.Count == 0)
            return new GaStartupDecision(system.Contract, fallbackRoute, GaStartupReason.MissingArtifact);

        GaStartupDecision? failure = null;
        var candidates = system.Artifacts.OrderBy(candidate =>
            ProfilePriority(candidate.Bundle?.Manifest.Requirements.ComputeProfile));
        foreach (var candidate in candidates)
        {
            var bundle = candidate.Bundle;
            if (bundle is null || bundle.Manifest.Contract != system.Contract)
            {
                failure ??= new GaStartupDecision(system.Contract, fallbackRoute, GaStartupReason.IncompatibleManifest,
                    candidate.Error ?? "Artifact contract does not match the registered system contract.",
                    ArtifactId: bundle?.ContentId);
                continue;
            }

            var requirements = bundle.Manifest.Requirements;
            foreach (var device in devices)
            {
                if (device.Format != bundle.Manifest.Toolchain.Format)
                    continue;

                var profile = device.Profiles.FirstOrDefault(profile => profile.Profile == requirements.ComputeProfile);
                var reason = CheckProfile(bundle.Manifest, profile, allowExperimental);
                string? detail = null;
                if (reason == GaStartupReason.None)
                {
                    var preparation = asynchronous
                        ? await device.PrepareAsync(bundle)
                        : device.Prepare(bundle);
                    if (preparation.Reason == GaStartupReason.None && preparation.Kernel is { } kernel)
                    {
                        kernels.Add(system.Contract.SystemId, kernel);
                        return new GaStartupDecision(system.Contract, GaRoute.Gpu, GaStartupReason.None,
                            Device: device.Info, ComputeProfile: requirements.ComputeProfile, ArtifactId: bundle.ContentId,
                            Qualification: profile!.Qualification);
                    }

                    preparation.Kernel?.Dispose();
                    reason = preparation.Reason switch
                    {
                        GaStartupReason.MissingCapability or GaStartupReason.IncompatibleManifest or
                        GaStartupReason.ResourceBudgetExceeded or GaStartupReason.ProbeFailed => preparation.Reason,
                        _ => GaStartupReason.PipelineCreationFailed
                    };
                    detail = preparation.Detail;
                }

                var attempt = new GaStartupDecision(system.Contract, fallbackRoute, reason, detail,
                    device.Info, requirements.ComputeProfile, bundle.ContentId, profile?.Qualification);
                if (failure is null || FailurePriority(attempt.Reason) > FailurePriority(failure.Reason))
                    failure = attempt;
            }
        }
        return failure ?? new GaStartupDecision(system.Contract, fallbackRoute, GaStartupReason.NoComputeApi);
    }

    private static GaStartupReason CheckProfile(
        GaArtifactManifest manifest, GaDeviceProfile? profile, bool allowExperimental)
    {
        var requirements = manifest.Requirements;
        if (profile is null || requirements.RequiredFeatures.Any(
                feature => !profile.Features.Contains(feature, StringComparer.Ordinal)))
            return GaStartupReason.MissingCapability;

        // Check per-axis limits first, then divide so oversized metadata cannot overflow a product.
        if (requirements.WorkgroupX > profile.MaxWorkgroupX || requirements.WorkgroupY > profile.MaxWorkgroupY ||
            requirements.WorkgroupZ > profile.MaxWorkgroupZ ||
            requirements.WorkgroupX > profile.MaxWorkgroupInvocations / requirements.WorkgroupY / requirements.WorkgroupZ ||
            requirements.SharedMemoryBytes > profile.MaxSharedMemoryBytes || requirements.StorageBytes > profile.MaxStorageBytes)
            return GaStartupReason.ResourceBudgetExceeded;

        if (profile.Qualification != GaQualification.Qualified &&
            !(allowExperimental && profile.Qualification == GaQualification.Experimental))
            return GaStartupReason.UnqualifiedConfiguration;
        return GaStartupReason.None;
    }

    private static int ProfilePriority(string? profile) => profile switch
    {
        GaComputeProfiles.Portable32 => 0,
        GaComputeProfiles.WideFixedEmulated => 1,
        GaComputeProfiles.NativeWide => 2,
        _ => int.MaxValue
    };

    private static int FailurePriority(GaStartupReason reason) => reason switch
    {
        GaStartupReason.ProbeFailed => 5,
        GaStartupReason.PipelineCreationFailed => 4,
        GaStartupReason.ResourceBudgetExceeded or GaStartupReason.UnqualifiedConfiguration => 3,
        GaStartupReason.MissingCapability => 2,
        _ => 1
    };

    internal static void DisposeKernels(Dictionary<string, IGaPreparedKernel> kernels)
    {
        List<Exception>? errors = null;
        foreach (var kernel in kernels.Values.Reverse())
        {
            try
            {
                kernel.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= new List<Exception>()).Add(error);
            }
        }
        kernels.Clear();
        if (errors is not null)
            throw new AggregateException("One or more GA kernels failed to release resources.", errors);
    }
}
