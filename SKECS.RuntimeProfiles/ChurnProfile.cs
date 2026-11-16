using System.Text.Json;

namespace SKECS.RuntimeProfiles;

/// <summary>A qualified profile for creating a new world with churn control.</summary>
public sealed class ChurnProfile
{
    public const string FormatId = "skecs-churn-profile-v1";
    public const int Version = 1;

    private readonly ChurnControlOptions _options;

    public ChurnProfile(string Format, int SchemaVersion, ProfileIdentity Identity,
        ChurnProfileSettings Settings, ProfileEvidence Evidence)
    {
        if (!string.Equals(Format, FormatId, StringComparison.Ordinal) || SchemaVersion != Version)
            throw new ArgumentException("Unsupported churn profile format or schema version.");

        this.Format = Format;
        this.SchemaVersion = SchemaVersion;
        this.Identity = Identity ?? throw new ArgumentNullException(nameof(Identity));
        this.Settings = Settings ?? throw new ArgumentNullException(nameof(Settings));
        this.Evidence = Evidence ?? throw new ArgumentNullException(nameof(Evidence));

        Identity.Validate();
        Evidence.Validate();
        _options = Settings.CreateOptions(Identity.ChunkCapacity);
    }

    public string Format { get; }
    public int SchemaVersion { get; }
    public ProfileIdentity Identity { get; }
    public ChurnProfileSettings Settings { get; }
    public ProfileEvidence Evidence { get; }

    /// <summary>Loads a fully compatible profile. The source stream remains open.</summary>
    /// <exception cref="InvalidDataException">The artifact is invalid or its identity differs.</exception>
    public static ChurnProfile Load(Stream source, ProfileIdentity expected)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (expected == null) throw new ArgumentNullException(nameof(expected));
        expected.Validate();
        expected.RequireCurrentEnvironment();

        ChurnProfile profile;
        try
        {
            profile = ChurnProfileJson.Read(source);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException and not ObjectDisposedException)
        {
            throw new InvalidDataException("Malformed churn profile JSON.", error);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(error.Message, error);
        }

        profile.Identity.RequireMatch(expected);
        return profile;
    }

    /// <summary>Loads a fully compatible profile or returns the reason the entire artifact was rejected.</summary>
    public static bool TryLoad(Stream source, ProfileIdentity expected, out ChurnProfile? profile, out string? reason)
    {
        try
        {
            profile = Load(source, expected);
            reason = null;
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            profile = null;
            reason = error.Message;
            return false;
        }
    }

    /// <summary>Creates options for the profile's chunk capacity and complete churn policy.</summary>
    public WorldOptions CreateOptions(bool diagnosticsEnabled = false)
    {
        Identity.RequireCurrentEnvironment();
        return new WorldOptions(Identity.ChunkCapacity, diagnosticsEnabled).WithChurnControl(_options);
    }

    /// <summary>Writes the versioned JSON artifact and leaves the destination open.</summary>
    public void Save(Stream destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        ChurnProfileJson.Write(destination, this);
    }
}
