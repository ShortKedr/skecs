using System.Text.Json;

namespace SKECS.RuntimeProfiles;

/// <summary>A qualified profile for creating a new world with churn control.</summary>
public sealed class ChurnProfile
{
    public const string FormatId = "skecs-churn-profile-v1";
    public const int Version = 1;
    public const string PackedFormatId = "skecs-churn-profile-v2";
    public const int PackedVersion = 2;

    private readonly ChurnControlOptions _options;
    private readonly ComponentPlacementOptions? _placement;

    public ChurnProfile(string Format, int SchemaVersion, ProfileIdentity Identity,
        ChurnProfileSettings Settings, ProfileEvidence Evidence)
        : this(Format, SchemaVersion, Identity, Settings, Evidence, null, Array.Empty<PackedProfileComponent>(), null) { }

    private ChurnProfile(string Format, int SchemaVersion, ProfileIdentity Identity,
        ChurnProfileSettings Settings, ProfileEvidence Evidence, PackedProfileEvidence? packedEvidence, PackedProfileComponent[] packedComponents,
        ComponentPlacementOptions? placement)
    {
        var supported = placement == null ? Format == FormatId && SchemaVersion == Version :
            Format == PackedFormatId && SchemaVersion == PackedVersion;
        if (!supported)
            throw new ArgumentException("Unsupported churn profile format or schema version.");

        this.Format = Format;
        this.SchemaVersion = SchemaVersion;
        this.Identity = Identity ?? throw new ArgumentNullException(nameof(Identity));
        this.Settings = Settings ?? throw new ArgumentNullException(nameof(Settings));
        this.Evidence = Evidence ?? throw new ArgumentNullException(nameof(Evidence));
        PackedComponents = Array.AsReadOnly(packedComponents);
        PackedEvidence = packedEvidence;
        _placement = placement;

        Identity.Validate();
        Evidence.Validate();
        PackedEvidence?.Validate();
        _options = Settings.CreateOptions(Identity.ChunkCapacity);
    }

    public string Format { get; }
    public int SchemaVersion { get; }
    public ProfileIdentity Identity { get; }
    public ChurnProfileSettings Settings { get; }
    public ProfileEvidence Evidence { get; }
    public PackedProfileEvidence? PackedEvidence { get; }
    public IReadOnlyList<PackedProfileComponent> PackedComponents { get; }

    public static ChurnProfile CreatePacked(ProfileIdentity identity, ChurnProfileSettings settings, ProfileEvidence evidence,
        PackedProfileEvidence placementEvidence, ComponentPlacementOptions permissions, IReadOnlyList<PackedProfileBinding> selected)
    {
        if (permissions == null) throw new ArgumentNullException(nameof(permissions));
        if (selected == null) throw new ArgumentNullException(nameof(selected));
        var bindings = selected.ToArray();
        var components = bindings.Select(binding => binding?.Description ??
            throw new ArgumentException("Selected binding is null.", nameof(selected))).ToArray();
        return CreatePacked(identity, settings, evidence, placementEvidence, components, permissions, bindings);
    }

    internal static ChurnProfile CreatePacked(ProfileIdentity identity, ChurnProfileSettings settings, ProfileEvidence evidence,
        PackedProfileEvidence placementEvidence, PackedProfileComponent[] components, ComponentPlacementOptions permissions,
        IReadOnlyList<PackedProfileBinding> bindings) =>
        new(PackedFormatId, PackedVersion, identity, settings, evidence,
            placementEvidence ?? throw new ArgumentNullException(nameof(placementEvidence)), components,
            PackedProfileBinding.Resolve(components, permissions, bindings));

    /// <summary>Loads a fully compatible profile. The source stream remains open.</summary>
    /// <exception cref="InvalidDataException">The artifact is invalid or its identity differs.</exception>
    public static ChurnProfile Load(Stream source, ProfileIdentity expected) => LoadCore(source, expected, null, null);

    /// <summary>Loads an archetype or packed profile against the application's pinned types and component registry.</summary>
    public static ChurnProfile Load(Stream source, ProfileIdentity expected, ComponentPlacementOptions permissions,
        IReadOnlyList<PackedProfileBinding> bindings)
    {
        if (permissions == null) throw new ArgumentNullException(nameof(permissions));
        if (bindings == null) throw new ArgumentNullException(nameof(bindings));
        return LoadCore(source, expected, permissions, bindings);
    }

    private static ChurnProfile LoadCore(Stream source, ProfileIdentity expected, ComponentPlacementOptions? permissions,
        IReadOnlyList<PackedProfileBinding>? bindings)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (expected == null) throw new ArgumentNullException(nameof(expected));
        expected.Validate();
        expected.RequireCurrentEnvironment();

        ChurnProfile profile;
        try
        {
            profile = ChurnProfileJson.Read(source, permissions, bindings);
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
    public static bool TryLoad(Stream source, ProfileIdentity expected, out ChurnProfile? profile, out string? reason) =>
        TryLoadCore(source, expected, null, null, out profile, out reason);

    public static bool TryLoad(Stream source, ProfileIdentity expected, ComponentPlacementOptions permissions,
        IReadOnlyList<PackedProfileBinding> bindings, out ChurnProfile? profile, out string? reason)
    {
        if (permissions == null) throw new ArgumentNullException(nameof(permissions));
        if (bindings == null) throw new ArgumentNullException(nameof(bindings));
        return TryLoadCore(source, expected, permissions, bindings, out profile, out reason);
    }

    private static bool TryLoadCore(Stream source, ProfileIdentity expected, ComponentPlacementOptions? permissions,
        IReadOnlyList<PackedProfileBinding>? bindings, out ChurnProfile? profile, out string? reason)
    {
        try
        {
            profile = LoadCore(source, expected, permissions, bindings);
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
        var options = new WorldOptions(Identity.ChunkCapacity, diagnosticsEnabled).WithChurnControl(_options);
        return _placement == null ? options : options.WithComponentPlacement(_placement);
    }

    /// <summary>Writes the versioned JSON artifact and leaves the destination open.</summary>
    public void Save(Stream destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        ChurnProfileJson.Write(destination, this);
    }
}
