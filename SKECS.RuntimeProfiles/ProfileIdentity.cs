using System.Runtime.InteropServices;

namespace SKECS.RuntimeProfiles;

/// <summary>The qualified application build, workload, schema, and execution environment.</summary>
public sealed record ProfileIdentity(string CoreVersion, string CoreBuildId, string BuildId,
    string WorkloadId, string SchemaFingerprint, string TargetId, string Runtime, string Os,
    string Architecture, int ChunkCapacity)
{
    /// <summary>Captures the running core and platform with the supplied application identities.</summary>
    /// <param name="buildId">The application's immutable build identity.</param>
    /// <param name="workloadId">The qualified workload identity.</param>
    /// <param name="schemaFingerprint">The SHA-256 fingerprint of the component schema and layout.</param>
    /// <param name="targetId">The application's qualified CPU and deployment target identity.</param>
    /// <param name="chunkCapacity">The application's requested chunk capacity.</param>
    public static ProfileIdentity Current(string buildId, string workloadId, string schemaFingerprint,
        string targetId, int chunkCapacity)
    {
        var core = typeof(World).Assembly;
        var identity = new ProfileIdentity(core.GetName().Version!.ToString(),
            core.ManifestModule.ModuleVersionId.ToString("D"), buildId, workloadId, schemaFingerprint, targetId,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), chunkCapacity);
        identity.Validate();
        return identity;
    }

    internal void Validate()
    {
        if (!System.Version.TryParse(CoreVersion, out _))
            throw new ArgumentException("A valid core assembly version is required.", nameof(CoreVersion));
        if (!Guid.TryParseExact(CoreBuildId, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("A valid core module identifier is required.", nameof(CoreBuildId));
        RequireText(BuildId, nameof(BuildId));
        RequireText(WorkloadId, nameof(WorkloadId));
        RequireSha256(SchemaFingerprint, nameof(SchemaFingerprint));
        RequireText(TargetId, nameof(TargetId));
        RequireText(Runtime, nameof(Runtime));
        RequireText(Os, nameof(Os));
        RequireText(Architecture, nameof(Architecture));
        if (ChunkCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(ChunkCapacity));
    }

    internal void RequireMatch(ProfileIdentity expected)
    {
        RequireEqual(CoreVersion, expected.CoreVersion, nameof(CoreVersion));
        RequireEqual(CoreBuildId, expected.CoreBuildId, nameof(CoreBuildId));
        RequireEqual(BuildId, expected.BuildId, nameof(BuildId));
        RequireEqual(WorkloadId, expected.WorkloadId, nameof(WorkloadId));
        RequireEqual(SchemaFingerprint, expected.SchemaFingerprint, nameof(SchemaFingerprint));
        RequireEqual(TargetId, expected.TargetId, nameof(TargetId));
        RequireEqual(Runtime, expected.Runtime, nameof(Runtime));
        RequireEqual(Os, expected.Os, nameof(Os));
        RequireEqual(Architecture, expected.Architecture, nameof(Architecture));
        if (ChunkCapacity != expected.ChunkCapacity)
            throw new InvalidDataException($"Profile identity mismatch for {nameof(ChunkCapacity)}.");
    }

    internal void RequireCurrentEnvironment() =>
        RequireMatch(Current(BuildId, WorkloadId, SchemaFingerprint, TargetId, ChunkCapacity));

    internal static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.", name);
    }

    internal static void RequireSha256(string value, string name)
    {
        if (value == null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A hexadecimal SHA-256 value is required.", name);
    }

    private static void RequireEqual(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Profile identity mismatch for {name}.");
    }
}
