using System.Security.Cryptography;
using SKECS;
using SKECS.RuntimeProfiles;

namespace Skecs.Samples.RuntimeProfiles;

internal static class Sample
{
    public static Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("Usage: runtime-profiles");

        const int profileChunkCapacity = 64;
        var identity = ProfileIdentity.Current("sample-build-v1", "sample-workload-v1",
            Convert.ToHexString(SHA256.HashData("sample-component-schema-v1"u8)), "sample-target", profileChunkCapacity);
        var settings = new ChurnProfileSettings(TargetRetainedBytes: 4096, StartupReserveBytes: 0,
            StartupBlocks: [], InitialEntityCapacity: 32, MaxOversizePercent: 25, ReleaseBatchSize: 4);

        // These fixture digests demonstrate the format. A deployed profile uses evidence from qualification.
        var evidence = new ProfileEvidence("sample-fixture-v1",
            Convert.ToHexString(SHA256.HashData("sample-corpus"u8)),
            Convert.ToHexString(SHA256.HashData("sample-verification"u8)),
            TrainingScenarios: 1, HoldoutScenarios: 1, Repetitions: 2);
        var fixture = new ChurnProfile(ChurnProfile.FormatId, ChurnProfile.Version, identity, settings, evidence);

        // The artifact pins the core build and environment, so create it for this run.
        using var json = new MemoryStream();
        fixture.Save(json);
        json.Position = 0;
        if (!ChurnProfile.TryLoad(json, identity, out var profile, out var reason) || profile is null)
            throw new InvalidDataException($"The example profile could not be loaded: {reason}");

        using var profiledWorld = new World(profile.CreateOptions());
        var profiledStats = profiledWorld.GetChurnControlStatistics();
        if (profiledWorld.ChunkCapacity != profileChunkCapacity || !profiledStats.Enabled ||
            profiledStats.TargetRetainedBytes != settings.TargetRetainedBytes)
            throw new InvalidOperationException("The world did not use the profile's settings.");
        Console.WriteLine($"Applied the illustrative profile: chunk capacity {profiledWorld.ChunkCapacity}, churn control enabled.");

        var differentWorkload = identity with { WorkloadId = "different-workload" };
        json.Position = 0;
        if (ChurnProfile.TryLoad(json, differentWorkload, out _, out var rejection) ||
            rejection?.Contains(nameof(ProfileIdentity.WorkloadId), StringComparison.Ordinal) != true)
            throw new InvalidDataException("Expected the profile to be rejected for a different workload.");

        Console.WriteLine(rejection);
        var defaults = new WorldOptions();
        using var fallbackWorld = new World(defaults);
        if (fallbackWorld.ChunkCapacity != defaults.ChunkCapacity || fallbackWorld.GetChurnControlStatistics().Enabled)
            throw new InvalidOperationException("The fallback world did not use the default settings.");
        Console.WriteLine($"Using default world settings: chunk capacity {fallbackWorld.ChunkCapacity}, churn control disabled.");
        return Task.CompletedTask;
    }
}
