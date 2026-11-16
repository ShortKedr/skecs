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
    public void ReductionBindingsAreVersionedAndMustMatchThePreparedArtifact()
    {
        var fixture = new Fixture();
        var contract = fixture.Contract with
        {
            EventCapacity = 2,
            EventTypeCount = 1,
            EventPayloadWords = 1,
            ReductionTypeCount = 1,
            SubsetVersion = GaProgram.SubsetVersion,
            IrVersion = GaProgram.IrVersion
        };
        var manifest = fixture.Manifest with { Contract = contract };
        Assert.Equal(1, GaArtifactManifest.Parse(manifest.GetCanonicalBytes()).Contract.ReductionTypeCount);
        Assert.Throws<InvalidDataException>(() =>
            (manifest with { Contract = contract with { ReductionTypeCount = 2 } }).GetCanonicalBytes());
        Assert.Throws<InvalidDataException>(() =>
            (manifest with { Contract = contract with { IrVersion = 14 } }).GetCanonicalBytes());

        var candidate = GaArtifactCandidate.Load(manifest.GetCanonicalBytes(), manifest.GetContentId(),
            fixture.Shader, fixture.Reflection, []);
        using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [candidate])], [new TestDevice()]);
        Assert.True(startup.Succeeded);
        Assert.Throws<InvalidDataException>(() => new GaBatchProcessor<byte>(
            startup, contract.SystemId, contract.SourceDigest, contract.IrDigest, contract.ComponentSchemaDigest, 1,
            static (_, _) => { }, static (_, _, _) => { }, contract.EventCapacity, null, null, false,
            [], [], default, null, [GaTypedEvent.For<uint>(static words => words[0], 1, [])]));
    }

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
    public void GlslRequiresDesktopProfileAndExplicitStorageNamespace()
    {
        var fixture = new Fixture();
        var manifest = fixture.Manifest with
        {
            Toolchain = fixture.Manifest.Toolchain with { Format = GaArtifactFormat.Glsl, TargetProfile = "glsl_430" },
            DeclaredBindings = fixture.Manifest.DeclaredBindings.Select(binding => binding with
            {
                Namespace = GaBindingNamespace.OpenGlShaderStorage, Space = 0, Count = 1
            }).ToArray()
        };
        Assert.NotNull(Load(manifest, "#version 430\nvoid main() {}\n"u8).Bundle);
        Assert.Null(Load(manifest, "#version 310 es\nvoid main() {}\n"u8).Bundle);
        Assert.Null(Load(manifest, "#version 430\n\0"u8).Bundle);
        Assert.Null(Load(manifest with { Toolchain = manifest.Toolchain with { TargetProfile = "glsl_es_310" } },
            "#version 430\nvoid main() {}\n"u8).Bundle);
        Assert.Throws<InvalidDataException>(() => (manifest with
        {
            DeclaredBindings = [manifest.DeclaredBindings[0] with { Space = 1 }, manifest.DeclaredBindings[1]]
        }).GetCanonicalBytes());

        GaArtifactCandidate Load(GaArtifactManifest value, ReadOnlySpan<byte> shader)
        {
            value = value with { ArtifactDigest = GaArtifactManifest.ComputeDigest(shader) };
            return GaArtifactCandidate.Load(value.GetCanonicalBytes(), value.GetContentId(), shader, fixture.Reflection, []);
        }
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
    public void StartupChecksDeviceWidthBeforePreparingAWorkgroupVariant()
    {
        var fixture = new Fixture();
        var manifest = fixture.Manifest with { Requirements = fixture.Manifest.Requirements with { WorkgroupX = 128 } };
        var contentId = manifest.GetContentId();
        Assert.NotEqual(fixture.ContentId, contentId);
        var wide = GaArtifactCandidate.Load(manifest.GetCanonicalBytes(), contentId, fixture.Shader, fixture.Reflection, []);
        Assert.NotNull(wide.Bundle);
        var device = new TestDevice();

        using var rejected = GaStartup.Select(GaPolicy.RequireGpu, [new(fixture.Contract, [wide])], [device]);
        Assert.Equal(GaStartupReason.ResourceBudgetExceeded, Assert.Single(rejected.Decisions).Reason);
        Assert.False(rejected.Succeeded);
        Assert.Equal(0, device.Prepared);

        using var supported = GaStartup.Select(GaPolicy.RequireGpu,
            [new(fixture.Contract, [wide, fixture.Load()])], [device]);
        Assert.Equal(GaRoute.Gpu, Assert.Single(supported.Decisions).Route);
        Assert.True(supported.Succeeded);
        Assert.Equal(1, device.Prepared);
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

    [Theory]
    [InlineData(99u, 0u, 0u)]
    [InlineData(GaOutputRecord.EmitInt32Kind, 9u, 1u)]
    [InlineData(GaOutputRecord.CreateEntityKind, 1u, 0u)]
    [InlineData(GaOutputRecord.DestroyTicketKind, 0u, 0u)]
    [InlineData(GaOutputRecord.DestroyTicketKind, 1u, 0u)]
    [InlineData(GaOutputRecord.DestroyTicketKind, uint.MaxValue, 0u)]
    [InlineData(GaOutputRecord.RemoveEntityBase, 0u, 1u)]
    [InlineData(GaOutputRecord.RemoveTicketBase, 0u, 0u)]
    public void InvalidMixedOutputIsRejectedBeforeAnyBatchWrite(uint kind, uint first, uint second)
    {
        var fixture = new Fixture(mixedOutputs: true);
        RejectBeforeWrite(fixture, () => new InvalidOutputKernel(kind, first, second), []);
    }

    [Theory]
    [InlineData(GaOutputRecord.AddEntityBase, 2u, 0u)]
    [InlineData(GaOutputRecord.AddEntityBase, 1u, 9u)]
    [InlineData(GaOutputRecord.AddTicketBase, 1u, 0u)]
    [InlineData(GaOutputRecord.AddEntityBase + 2, 1u, 0u)]
    public void InvalidAdditionIsRejectedBeforeAnyBatchWrite(uint kind, uint value, uint padding)
    {
        var fixture = new Fixture(mixedOutputs: true, additions: 2, additionWords: 2);
        GaComponentAddition[] bindings =
        [
            GaComponentAddition.For(static words => new FlagComponent(words[0] != 0), 1, [0]),
            GaComponentAddition.For(static words => new PairComponent((int)words[0], (int)words[1]), 2, []),
        ];
        RejectBeforeWrite(fixture, () => new InvalidAdditionKernel(kind, value, padding), bindings);
    }

    private static void RejectBeforeWrite(Fixture fixture, Func<IGaPreparedKernel> kernel,
        ReadOnlySpan<GaComponentAddition> bindings)
    {
        var device = new TestDevice { CreateKernel = kernel };
        using var startup = GaStartup.Select(GaPolicy.RequireGpu,
            [new(fixture.Contract, [fixture.Load()])], [device]);
        var writes = 0;
        var identity = fixture.Contract;
        var processor = new GaBatchProcessor<byte>(startup, identity.SystemId, identity.SourceDigest,
            identity.IrDigest, identity.ComponentSchemaDigest, 1, static (_, _) => { },
            (_, count, _) => writes += count, identity.EventCapacity, observe: null, snapshot: null, usesCommands: true,
            componentRemovals: [], componentAdditions: bindings);
        using var world = new World();
        world.CreateEntity(new CommandTestComponent(1));
        world.CreateEntity(new CommandTestComponent(2));
        using var runner = new DeterministicRunner<byte>(world,
            new DeterministicWorkload<byte>().AddBatchSystem(SystemStage.Update,
                new DeterministicQuery().With<CommandTestComponent>(), processor));
        runner.BeginTick(1, 0, SKECS.Numerics.Fixed.One);
        Assert.True(System.Threading.SpinWait.SpinUntil(() =>
            runner.Advance(1) != DeterministicTickStatus.Pending, TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidDataException>(runner.Fault);
        Assert.Equal(0, writes);
        Assert.Null(runner.LastCompleted);
    }

    private readonly record struct CommandTestComponent(int Value);
    private readonly record struct FlagComponent(bool Value);
    private readonly record struct PairComponent(int First, int Second);

    private sealed class InvalidOutputKernel(uint kind, uint first, uint second) : GaPackedKernel(64, 1, 128, [])
    {
        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            uint[] packet =
            [
                GaEventPacket.Version, 2, 2, GaOutputRecord.Words,
                1, GaOutputRecord.CreateEntityKind, 0, 0, 0, 0, 0,
                2, GaOutputRecord.EmitInt32Kind, 7, 0, kind, first, second
            ];
            packet.CopyTo(output, 2);
        }

        protected override void Release() { }
    }

    private sealed class InvalidAdditionKernel(uint kind, uint value, uint padding) : GaPackedKernel(64, 1, 128, [])
    {
        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            uint[] packet =
            [
                GaEventPacket.Version, 2, 2, GaOutputRecord.Words + 2,
                1, GaOutputRecord.CreateEntityKind, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                1, kind, 0, 0, value, padding, 0, 0, 0, 0, 0
            ];
            packet.CopyTo(output, 2);
        }

        protected override void Release() { }
    }

    private sealed class Fixture
    {
        public Fixture(string systemId = "Movement", bool mixedOutputs = false, int additions = 0, int additionWords = 0)
        {
            var digest = GaArtifactManifest.ComputeDigest(Shader);
            Contract = mixedOutputs
                ? new GaContractIdentity(systemId, "test/v1", digest, digest, digest,
                    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
                    { EventCapacity = 2, UsesCommands = true, ComponentAdditionCount = additions,
                        AdditionPayloadWords = additionWords }
                : new GaContractIdentity(systemId, "test/v1", digest, digest, digest, 1, 1, 1);
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
        public Func<IGaPreparedKernel>? CreateKernel { get; init; }
        public GaQualification Qualification { get; init; } = GaQualification.Qualified;
        public GaStartupReason Failure { get; set; }
        public GaArtifactFormat Format => GaArtifactFormat.SpirV;
        public GaDeviceInfo Info { get; } = new("test", "Vulkan", "1.2", "test", "test");
        public IReadOnlyList<GaDeviceProfile> Profiles =>
            [new(GaComputeProfiles.Portable32, Qualification, ["storage"], 64, 64, 64, 64, 0, 16)];

        public GaPreparation Prepare(GaArtifactBundle artifact)
        {
            Prepared++;
            return new GaPreparation(Failure == GaStartupReason.None ? CreateKernel?.Invoke() ?? new TestHandle(this) : null, Failure);
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
