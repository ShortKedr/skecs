using System.Security.Cryptography;
using SKECS;
using SKECS.RuntimeProfiles;

namespace Skecs.Samples.RuntimeProfiles;

internal static class Sample
{
    private const int ProfileChunkCapacity = 64;

    public static Task RunAsync(string[] args)
    {
        if (args.Length != 0)
        {
            throw new ArgumentException("Usage: runtime-profiles");
        }

        // A profile pins the core build and environment, so create the fixture for this run.
        var fixture = CreateProfileFixture();
        using var json = new MemoryStream();
        fixture.Save(json);
        json.Position = 0;

        if (!ChurnProfile.TryLoad(json, fixture.Identity, out var profile, out var reason) || profile is null)
        {
            throw new InvalidDataException($"The example profile could not be loaded: {reason}");
        }

        using var profiledWorld = new World(profile.CreateOptions());
        var churnStatistics = profiledWorld.GetChurnControlStatistics();
        if (profiledWorld.ChunkCapacity != ProfileChunkCapacity ||
            !churnStatistics.Enabled ||
            churnStatistics.TargetRetainedBytes != fixture.Settings.TargetRetainedBytes)
        {
            throw new InvalidOperationException("The world did not use the profile's settings.");
        }

        Console.WriteLine(
            $"Applied the illustrative profile: chunk capacity {profiledWorld.ChunkCapacity}, churn control enabled.");

        // A different workload must use its own profile or fall back to default settings.
        var differentWorkload = fixture.Identity with { WorkloadId = "different-workload" };
        json.Position = 0;
        if (ChurnProfile.TryLoad(json, differentWorkload, out _, out var rejection) ||
            rejection?.Contains(nameof(ProfileIdentity.WorkloadId), StringComparison.Ordinal) != true)
        {
            throw new InvalidDataException("Expected the profile to be rejected for a different workload.");
        }

        Console.WriteLine(rejection);
        var defaults = new WorldOptions();
        using var fallbackWorld = new World(defaults);
        if (fallbackWorld.ChunkCapacity != defaults.ChunkCapacity || fallbackWorld.GetChurnControlStatistics().Enabled)
        {
            throw new InvalidOperationException("The fallback world did not use the default settings.");
        }

        Console.WriteLine(
            $"Using default world settings: chunk capacity {fallbackWorld.ChunkCapacity}, churn control disabled.");
        return Task.CompletedTask;
    }

    private static ChurnProfile CreateProfileFixture()
    {
        var componentSchemaDigest = Convert.ToHexString(SHA256.HashData("sample-component-schema-v1"u8));
        var identity = ProfileIdentity.Current(
            "sample-build-v1",
            "sample-workload-v1",
            componentSchemaDigest,
            "sample-target",
            ProfileChunkCapacity);
        var settings = new ChurnProfileSettings(
            TargetRetainedBytes: 4096,
            StartupReserveBytes: 0,
            StartupBlocks: [],
            InitialEntityCapacity: 32,
            MaxOversizePercent: 25,
            ReleaseBatchSize: 4);

        // These fixture digests demonstrate the format. Deployed profiles use evidence from qualification.
        var evidence = new ProfileEvidence(
            "sample-fixture-v1",
            Convert.ToHexString(SHA256.HashData("sample-corpus"u8)),
            Convert.ToHexString(SHA256.HashData("sample-verification"u8)),
            TrainingScenarios: 1,
            HoldoutScenarios: 1,
            Repetitions: 2);

        return new ChurnProfile(ChurnProfile.FormatId, ChurnProfile.Version, identity, settings, evidence);
    }
}
