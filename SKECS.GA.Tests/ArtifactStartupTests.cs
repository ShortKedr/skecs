using System;
using System.IO;
using System.Linq;
using System.Text;
using SKECS.GA;
using Xunit;

namespace SKECS.GA.Tests;

public sealed class ArtifactStartupTests
{
    [Fact]
    public void ManifestNormalizesOrderAndPinsPhysicalBindings()
    {
        var fixture = new Fixture();
        var manifest = fixture.Manifest;
        var permuted = manifest with
        {
            DeclaredBindings = manifest.DeclaredBindings.Reverse().ToArray(),
            EntryPointUsage = manifest.EntryPointUsage.Reverse().ToArray()
        };
        Assert.Equal(fixture.ContentId, permuted.GetContentId());
        Assert.Equal(fixture.ContentId, GaArtifactManifest.Parse(fixture.Json).GetContentId());

        var output = manifest.DeclaredBindings[0];
        var input = manifest.DeclaredBindings[1];
        Assert.Null(fixture.Load(manifest with { DeclaredBindings = [output with { Index = 4 }, input] }).Bundle);
        Assert.Throws<InvalidDataException>(() =>
            (manifest with { DeclaredBindings = [output with { Index = input.Index }, input] }).GetContentId());
        Assert.Throws<InvalidDataException>(() =>
            (manifest with { DeclaredBindings = [output with { Layout = output.Layout! with { ElementStride = 7 } }, input] })
            .GetContentId());
        Assert.Throws<InvalidDataException>(() =>
            (manifest with { EntryPointUsage = [manifest.EntryPointUsage[0]] }).GetContentId());

        var missingUse = Encoding.UTF8.GetString(fixture.Json).Replace(",\"Used\":false", "");
        Assert.Null(GaArtifactCandidate.Load(Encoding.UTF8.GetBytes(missingUse),
            fixture.ContentId, fixture.Shader, fixture.Reflection, []).Bundle);
    }

    [Fact]
    public void BundleRejectsCorruptionAndOwnsItsByteCopies()
    {
        var fixture = new Fixture();
        var rawHlsl = "float4 Main() : SV_Target { return 0; }"u8.ToArray();
        var wrongFormat = fixture.Manifest with { ArtifactDigest = GaArtifactManifest.ComputeDigest(rawHlsl) };
        Assert.Null(GaArtifactCandidate.Load(wrongFormat.GetCanonicalBytes(), wrongFormat.GetContentId(),
            rawHlsl, fixture.Reflection, []).Bundle);
        Assert.Null(GaArtifactCandidate.Load(fixture.Json, fixture.ContentId, [0, 2, 3], fixture.Reflection, []).Bundle);
        Assert.Null(GaArtifactCandidate.Load(fixture.Json, fixture.ContentId, fixture.Shader, [9, 5], []).Bundle);

        var bundle = Assert.IsType<GaArtifactBundle>(fixture.Load().Bundle);
        Assert.True(bundle.Artifact.SequenceEqual(fixture.Shader));
        fixture.Shader[0] = 7;
        Assert.Equal(3, bundle.Artifact[0]);
    }

    [Fact]
    public void StartupDistinguishesCpuPolicyAndInvalidSelectedArtifact()
    {
        var fixture = new Fixture();
        var device = new TestDevice();
        var wrongIdentity = fixture.Contract with { NumericVersion = 2 };
        var request = new GaSystemRequest(wrongIdentity, [fixture.Load()]);

        using var cpu = GaStartup.Select(GaPolicy.CpuOnly, [request], [device]);
        Assert.Equal(GaSelectedPlan.CpuOnly, cpu.SelectedPlan);
        Assert.Equal(GaStartupReason.CpuOnlyPolicy, Assert.Single(cpu.Decisions).Reason);

        using var automatic = GaStartup.Select(GaPolicy.Auto, [request], [device]);
        Assert.Equal(GaSelectedPlan.CpuOnly, automatic.SelectedPlan);
        Assert.Equal(GaStartupReason.IncompatibleManifest, Assert.Single(automatic.Decisions).Reason);

        using var strict = GaStartup.Select(GaPolicy.RequireGpu, [request], [device]);
        Assert.Equal(GaSelectedPlan.StartupFailure, strict.SelectedPlan);
        Assert.Equal(GaStartupReason.IncompatibleManifest, Assert.Single(strict.Decisions).Reason);
        Assert.Equal(0, device.Prepared);
    }

    [Fact]
    public void StartupReportsUnavailableExperimentalAndFailedProbes()
    {
        var fixture = new Fixture();
        var request = new GaSystemRequest(fixture.Contract, [fixture.Load()]);
        using var absent = GaStartup.Select(GaPolicy.Auto, [request], []);
        Assert.Equal(GaStartupReason.NoComputeApi, Assert.Single(absent.Decisions).Reason);

        var device = new TestDevice { Qualification = GaQualification.Experimental };
        using var automatic = GaStartup.Select(GaPolicy.Auto, [request], [device]);
        Assert.Equal(GaStartupReason.UnqualifiedConfiguration, Assert.Single(automatic.Decisions).Reason);
        Assert.Equal(0, device.Prepared);

        device.Failure = GaStartupReason.ProbeFailed;
        using var failedProbe = GaStartup.Select(GaPolicy.RequireGpu, [request], [device], allowExperimental: true);
        Assert.Equal(GaStartupReason.ProbeFailed, Assert.Single(failedProbe.Decisions).Reason);
        Assert.False(failedProbe.Succeeded);
        Assert.Equal(1, device.Prepared);
    }

    [Fact]
    public void RequireGpuReleasesPreparedPipelinesIfAnyComputeSystemFailsStartup()
    {
        var fixture = new Fixture();
        var request = new GaSystemRequest(fixture.Contract, [fixture.Load()]);
        var apply = new GaSystemRequest(fixture.Contract with { SystemId = "Apply" }, []);
        var device = new TestDevice();

        using var failed = GaStartup.Select(GaPolicy.RequireGpu, [request, apply], [device]);
        Assert.Equal(GaSelectedPlan.StartupFailure, failed.SelectedPlan);
        Assert.Equal(GaStartupReason.MissingArtifact, failed.Decisions[1].Reason);
        Assert.Throws<InvalidOperationException>(() => failed.GetPreparedKernel(request.Contract.SystemId));
        Assert.Equal(1, device.Disposed);

        using (var mixed = GaStartup.Select(GaPolicy.RequireGpu, [request, apply with { DeclaredCpuStage = true }], [device]))
        {
            Assert.Equal(GaSelectedPlan.Mixed, mixed.SelectedPlan);
            Assert.Equal(GaStartupReason.DeclaredCpuStage, mixed.Decisions[1].Reason);
            Assert.NotNull(mixed.GetPreparedKernel(request.Contract.SystemId));
        }
        Assert.Equal(2, device.Disposed);
    }

    [Fact]
    public void DisposalReleasesRemainingKernelsAndAggregatesFailuresOnce()
    {
        var fixture = new Fixture();
        var second = new Fixture("Second");
        var device = new TestDevice { ThrowOnFirstDispose = true };
        var result = GaStartup.Select(GaPolicy.RequireGpu,
            [new(fixture.Contract, [fixture.Load()]), new(second.Contract, [second.Load()])], [device]);
        Assert.True(result.Succeeded);
        var error = Assert.Throws<AggregateException>(result.Dispose);
        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
        Assert.Equal(2, device.Disposed);
        result.Dispose();
        Assert.Equal(2, device.Disposed);
    }

    private sealed class Fixture
    {
        public Fixture(string systemId = "Movement")
        {
            var digest = GaArtifactManifest.ComputeDigest(Shader);
            Contract = new GaContractIdentity(systemId, "test/v1", digest, digest, digest, 1, 1, 1);
            var tool = new GaToolchain("2025.6.1", "SPIRV", GaArtifactFormat.SpirV, "spirv_1_3", [], ["B=1", "A=0"], []);
            var layout = new GaResourceLayout(8, 4, 8, [new("x", 0, 4, 0, 0), new("y", 4, 4, 0, 0)]);
            GaResourceBinding[] bindings =
            [
                new("output", GaBindingNamespace.VulkanDescriptor, 1, 3, 1, GaResourceKind.StorageBuffer,
                    GaResourceAccess.Write, layout),
                new("input", GaBindingNamespace.VulkanDescriptor, 1, 2, 3, GaResourceKind.StorageBuffer,
                    GaResourceAccess.Read, layout)
            ];
            var requirements = new GaKernelRequirements(GaComputeProfiles.Portable32, ["storage"], 64, 1, 1, 0, 16);
            Manifest = new GaArtifactManifest(1, Contract, tool, "main", bindings,
                [new("output", true), new("input", false)], requirements,
                digest, GaArtifactManifest.ComputeDigest(Reflection), GaArtifactManifest.ComputeDigest([]));
            ContentId = Manifest.GetContentId();
            Json = Manifest.GetCanonicalBytes();
        }

        public byte[] Shader { get; } = [3, 2, 35, 7, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];
        public byte[] Reflection { get; } = [4, 5];
        public GaContractIdentity Contract { get; }
        public GaArtifactManifest Manifest { get; }
        public string ContentId { get; }
        public byte[] Json { get; }

        public GaArtifactCandidate Load(GaArtifactManifest? variant = null) => GaArtifactCandidate.Load(
            variant?.GetCanonicalBytes() ?? Json, ContentId, Shader, Reflection, []);
    }

    private sealed class TestDevice : IGaDevice
    {
        public int Prepared { get; private set; }
        public int Disposed { get; private set; }
        public bool ThrowOnFirstDispose { get; init; }
        public GaQualification Qualification { get; init; } = GaQualification.Qualified;
        public GaStartupReason Failure { get; set; }
        public GaArtifactFormat Format => GaArtifactFormat.SpirV;
        public GaDeviceInfo Info { get; } = new("test", "Vulkan", "1.2", "test", "test");
        public IReadOnlyList<GaDeviceProfile> Profiles =>
            [new(GaComputeProfiles.Portable32, Qualification, ["storage"], 64, 64, 64, 64, 0, 16)];

        public GaPreparation Prepare(GaArtifactBundle artifact)
        {
            Prepared++;
            return new GaPreparation(Failure == GaStartupReason.None ? new TestHandle(this) : null, Failure);
        }

        private sealed class TestHandle(TestDevice device) : IGaPreparedKernel
        {
            public void Dispose()
            {
                device.Disposed++;
                if (device.ThrowOnFirstDispose && device.Disposed == 1)
                    throw new InvalidOperationException("Test cleanup failure.");
            }
        }
    }
}
