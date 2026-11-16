using System.Text;
using SKECS.RuntimeProfiles;

namespace SKECS.RuntimeProfiles.Tests;

public sealed class ChurnProfileTests
{
    private static readonly string SchemaHash = new('a', 64);
    private static readonly ProfileIdentity Identity = ProfileIdentity.Current("build-1", "levels-1", SchemaHash, "test-cpu", 64);
    private static readonly ProfileEvidence Evidence = new("test-verifier/1", new string('b', 64), new string('c', 64), 3, 2, 4);

    [Fact]
    public void JsonRoundTripCreatesWorldWithEntirePolicy()
    {
        var original = MakeProfile();
        using var saved = Write(original);
        Assert.True(ChurnProfile.TryLoad(saved, Identity, out var loaded, out var reason));
        Assert.Null(reason);
        Assert.True(saved.CanRead);
        var profile = Assert.IsType<ChurnProfile>(loaded);
        Assert.Equal(Identity, profile.Identity);
        Assert.Equal(Evidence, profile.Evidence);
        Assert.Equal(original.Settings.StartupBlocks, profile.Settings.StartupBlocks);

        var options = profile.CreateOptions(diagnosticsEnabled: true);
        Assert.Equal(64, options.ChunkCapacity);
        Assert.True(options.DiagnosticsEnabled);
        var churn = Assert.IsType<ChurnControlOptions>(options.ChurnControl);
        Assert.Equal(4096, churn.TargetRetainedBytes);
        Assert.Equal(1118, churn.StartupReserveBytes);
        Assert.Equal(128, churn.InitialEntityCapacity);
        Assert.Equal(10, churn.MaxOversizePercent);
        Assert.Equal(3, churn.ReleaseBatchSize);
        using var world = new World(options);
        var startup = world.GetChurnControlStatistics();
        Assert.True(startup.Enabled);
        Assert.Equal(churn.TargetRetainedBytes, startup.TargetRetainedBytes);
        Assert.Equal(churn.StartupReserveBytes, startup.ReusableEmptyBytes);
        Assert.Equal(2, startup.AllocationCount);
        var entity = world.CreateEntity(42);
        Assert.Equal(42, world.Get<int>(entity));

        using var resaved = Write(profile);
        Assert.Equal(saved.ToArray(), resaved.ToArray());
    }

    [Fact]
    public void StartupBlockCollectionCannotBeChangedAfterValidation()
    {
        var blocks = new[] { new ChurnStartupBlock(47, 2) };
        var profile = MakeProfile(new ChurnProfileSettings(4096, 1118, blocks, 128, 10, 3));
        blocks[0] = default;
        profile.Settings.StartupBlocks[0] = default;
        Assert.Equal(47, profile.Settings.StartupBlocks[0].NativeBytes);
        Assert.Equal(2, profile.Settings.StartupBlocks[0].Count);
    }

    [Theory]
    [InlineData("BuildId")]
    [InlineData("WorkloadId")]
    [InlineData("SchemaFingerprint")]
    [InlineData("TargetId")]
    [InlineData("CoreVersion")]
    [InlineData("CoreBuildId")]
    [InlineData("Runtime")]
    [InlineData("Os")]
    [InlineData("Architecture")]
    [InlineData("ChunkCapacity")]
    public void StaleIdentityRejectsEntireArtifact(string field)
    {
        var stale = field switch
        {
            "BuildId" => Identity with { BuildId = "build-2" },
            "WorkloadId" => Identity with { WorkloadId = "levels-2" },
            "SchemaFingerprint" => Identity with { SchemaFingerprint = new string('d', 64) },
            "TargetId" => Identity with { TargetId = "test-other-cpu" },
            "CoreVersion" => Identity with { CoreVersion = "99.99.0.0" },
            "CoreBuildId" => Identity with { CoreBuildId = "e99e3ef8-4bb8-4ded-a878-ab523561ac96" },
            "Runtime" => Identity with { Runtime = "other runtime" },
            "Os" => Identity with { Os = "other OS" },
            "Architecture" => Identity with { Architecture = "other architecture" },
            "ChunkCapacity" => Identity with { ChunkCapacity = 32 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        using var saved = Write(MakeProfile(identity: stale));
        Assert.False(ChurnProfile.TryLoad(saved, Identity, out var profile, out var reason));
        Assert.Null(profile);
        Assert.Contains(field, reason);
    }

    [Fact]
    public void AnExpectedIdentityCannotOverrideTheRunningEnvironment()
    {
        var stale = Identity with { CoreBuildId = "e99e3ef8-4bb8-4ded-a878-ab523561ac96" };
        var profile = MakeProfile(identity: stale);
        using var saved = Write(profile);
        Assert.Throws<InvalidDataException>(() => ChurnProfile.Load(saved, stale));
        Assert.Throws<InvalidDataException>(() => profile.CreateOptions());
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"placement\": {}")]
    [InlineData("\"repetitions\": 4", "\"repetitions\": 1")]
    [InlineData("\"holdoutScenarios\": 2", "\"holdoutScenarios\": 0")]
    [InlineData("\"repetitions\": 4", "\"repetitions\": 4e0")]
    [InlineData("\"repetitions\": 4", "\"repetitions\": null")]
    [InlineData("\"count\": 2", "\"count\": 0")]
    [InlineData("\"startupReserveBytes\": 1118", "\"startupReserveBytes\": 1117")]
    [InlineData("\"targetRetainedBytes\": 4096", "\"targetRetainedBytes\": 1117")]
    [InlineData("\"targetRetainedBytes\": 4096", "\"targetRetainedBytes\": 9223372036854775808")]
    [InlineData("\"verifierVersion\": \"test-verifier/1\"", "\"verifierVersion\": \"\\uD800\"")]
    [InlineData("\"verifierVersion\": \"test-verifier/1\"", "\"verifierVersio\\uD800\": \"test-verifier/1\"")]
    [InlineData("\"repetitions\": 4", "\"repetitions\":")]
    public void MalformedOrUnsupportedDataIsRejected(string valid, string invalid)
    {
        using var saved = Write(MakeProfile());
        var json = Encoding.UTF8.GetString(saved.ToArray());
        Assert.Contains(valid, json);
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(json.Replace(valid, invalid, StringComparison.Ordinal)));
        Assert.False(ChurnProfile.TryLoad(source, Identity, out var profile, out var reason));
        Assert.Null(profile);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void InvalidEvidenceDigestIsRejected()
    {
        using var saved = Write(MakeProfile());
        var json = Encoding.UTF8.GetString(saved.ToArray()).Replace(Evidence.VerificationSha256, "invalid-hash", StringComparison.Ordinal);
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => ChurnProfile.Load(source, Identity));
    }

    private static ChurnProfile MakeProfile(ChurnProfileSettings? settings = null, ProfileIdentity? identity = null) =>
        new(ChurnProfile.FormatId, ChurnProfile.Version, identity ?? Identity,
            settings ?? new ChurnProfileSettings(4096, 1118, new[] { new ChurnStartupBlock(47, 2) }, 128, 10, 3), Evidence);

    private static MemoryStream Write(ChurnProfile profile)
    {
        var stream = new MemoryStream();
        profile.Save(stream);
        stream.Position = 0;
        return stream;
    }
}
