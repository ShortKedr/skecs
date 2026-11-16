using System.Text.Json;

namespace SKECS.RuntimeProfiles;

internal static class ChurnProfileJson
{
    internal static ChurnProfile Read(Stream source, ComponentPlacementOptions? permissions, IReadOnlyList<PackedProfileBinding>? bindings)
    {
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        var packed = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out var value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var schema) && schema == ChurnProfile.PackedVersion;
        if (packed) RequireObject(root, "profile", "format", "schemaVersion", "identity", "settings", "evidence", "placement", "placementEvidence");
        else RequireObject(root, "profile", "format", "schemaVersion", "identity", "settings", "evidence");
        var format = ReadString(root, "format");
        var version = ReadInt(root, "schemaVersion");
        var expectedFormat = packed ? ChurnProfile.PackedFormatId : ChurnProfile.FormatId;
        var expectedVersion = packed ? ChurnProfile.PackedVersion : ChurnProfile.Version;
        if (format != expectedFormat || version != expectedVersion)
            throw new InvalidDataException("Unsupported churn profile format or schema version.");

        var identity = ReadIdentity(root.GetProperty("identity"));
        var settings = ReadSettings(root.GetProperty("settings"));
        var evidence = ReadEvidence(root.GetProperty("evidence"));
        if (!packed) return new ChurnProfile(format, version, identity, settings, evidence);
        if (permissions == null || bindings == null)
            throw new InvalidDataException("A packed profile requires application placement permissions and type bindings.");
        var controls = root.GetProperty("placementEvidence");
        RequireObject(controls, "placement evidence", "samePolicySha256", "tunedArchetypesSha256");
        var placementEvidence = new PackedProfileEvidence(ReadString(controls, "samePolicySha256"), ReadString(controls, "tunedArchetypesSha256"));
        return ChurnProfile.CreatePacked(identity, settings, evidence, placementEvidence,
            ReadPlacement(root.GetProperty("placement")), permissions, bindings);
    }

    private static PackedProfileComponent[] ReadPlacement(JsonElement placement)
    {
        if (placement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("placement must be an array.");
        var components = new PackedProfileComponent[placement.GetArrayLength()];
        var index = 0;
        foreach (var component in placement.EnumerateArray())
        {
            RequireObject(component, "packed component", "assembly", "type", "size", "alignment", "schemaFingerprint");
            components[index++] = new PackedProfileComponent(ReadString(component, "assembly"), ReadString(component, "type"),
                ReadInt(component, "size"), ReadInt(component, "alignment"), ReadString(component, "schemaFingerprint"));
        }
        return components;
    }

    private static ProfileIdentity ReadIdentity(JsonElement value)
    {
        RequireObject(value, "identity", "coreVersion", "coreBuildId", "buildId", "workloadId", "schemaFingerprint",
            "targetId", "runtime", "os", "architecture", "chunkCapacity");
        return new ProfileIdentity(ReadString(value, "coreVersion"), ReadString(value, "coreBuildId"),
            ReadString(value, "buildId"), ReadString(value, "workloadId"), ReadString(value, "schemaFingerprint"),
            ReadString(value, "targetId"), ReadString(value, "runtime"), ReadString(value, "os"),
            ReadString(value, "architecture"), ReadInt(value, "chunkCapacity"));
    }

    private static ChurnProfileSettings ReadSettings(JsonElement value)
    {
        RequireObject(value, "settings", "targetRetainedBytes", "startupReserveBytes", "startupBlocks",
            "initialEntityCapacity", "maxOversizePercent", "releaseBatchSize");
        var blocks = value.GetProperty("startupBlocks");
        if (blocks.ValueKind != JsonValueKind.Array) throw new InvalidDataException("startupBlocks must be an array.");
        var startupBlocks = new ChurnStartupBlock[blocks.GetArrayLength()];
        var index = 0;
        foreach (var block in blocks.EnumerateArray())
        {
            RequireObject(block, "startup block", "nativeBytes", "count");
            startupBlocks[index++] = new ChurnStartupBlock(ReadInt(block, "nativeBytes"), ReadInt(block, "count"));
        }

        return new ChurnProfileSettings(ReadLong(value, "targetRetainedBytes"), ReadLong(value, "startupReserveBytes"),
            startupBlocks, ReadInt(value, "initialEntityCapacity"), ReadInt(value, "maxOversizePercent"),
            ReadInt(value, "releaseBatchSize"));
    }

    private static ProfileEvidence ReadEvidence(JsonElement value)
    {
        RequireObject(value, "evidence", "verifierVersion", "corpusSha256", "verificationSha256",
            "trainingScenarios", "holdoutScenarios", "repetitions");
        return new ProfileEvidence(ReadString(value, "verifierVersion"), ReadString(value, "corpusSha256"),
            ReadString(value, "verificationSha256"), ReadInt(value, "trainingScenarios"),
            ReadInt(value, "holdoutScenarios"), ReadInt(value, "repetitions"));
    }

    private static void RequireObject(JsonElement value, string context, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context} must be an object.");
        var missing = new HashSet<string>(properties, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!missing.Remove(property.Name))
                throw new InvalidDataException($"Unknown or duplicate property '{property.Name}' in {context}.");
        }
        if (missing.Count != 0)
            throw new InvalidDataException($"Missing required properties in {context}: {string.Join(", ", missing)}.");
    }

    private static string ReadString(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        return property.ValueKind == JsonValueKind.String ? property.GetString()! :
            throw new InvalidDataException($"'{name}' must be a string.");
    }

    private static int ReadInt(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value :
            throw new InvalidDataException($"'{name}' must be a 32-bit integer.");
    }

    private static long ReadLong(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value) ? value :
            throw new InvalidDataException($"'{name}' must be a 64-bit integer.");
    }

    internal static void Write(Stream destination, ChurnProfile profile)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", profile.Format);
        writer.WriteNumber("schemaVersion", profile.SchemaVersion);
        WriteIdentity(writer, profile.Identity);
        WriteSettings(writer, profile.Settings);
        WriteEvidence(writer, profile.Evidence);
        if (profile.PackedEvidence is { } controls)
        {
            writer.WriteStartArray("placement");
            foreach (var component in profile.PackedComponents)
            {
                writer.WriteStartObject();
                writer.WriteString("assembly", component.Assembly);
                writer.WriteString("type", component.Type);
                writer.WriteNumber("size", component.Size);
                writer.WriteNumber("alignment", component.Alignment);
                writer.WriteString("schemaFingerprint", component.SchemaFingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("placementEvidence");
            writer.WriteString("samePolicySha256", controls.SamePolicySha256);
            writer.WriteString("tunedArchetypesSha256", controls.TunedArchetypesSha256);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteIdentity(Utf8JsonWriter writer, ProfileIdentity identity)
    {
        writer.WriteStartObject("identity");
        writer.WriteString("coreVersion", identity.CoreVersion);
        writer.WriteString("coreBuildId", identity.CoreBuildId);
        writer.WriteString("buildId", identity.BuildId);
        writer.WriteString("workloadId", identity.WorkloadId);
        writer.WriteString("schemaFingerprint", identity.SchemaFingerprint);
        writer.WriteString("targetId", identity.TargetId);
        writer.WriteString("runtime", identity.Runtime);
        writer.WriteString("os", identity.Os);
        writer.WriteString("architecture", identity.Architecture);
        writer.WriteNumber("chunkCapacity", identity.ChunkCapacity);
        writer.WriteEndObject();
    }

    private static void WriteSettings(Utf8JsonWriter writer, ChurnProfileSettings settings)
    {
        writer.WriteStartObject("settings");
        writer.WriteNumber("targetRetainedBytes", settings.TargetRetainedBytes);
        writer.WriteNumber("startupReserveBytes", settings.StartupReserveBytes);
        writer.WriteStartArray("startupBlocks");
        foreach (var block in settings.StartupBlocks)
        {
            writer.WriteStartObject();
            writer.WriteNumber("nativeBytes", block.NativeBytes);
            writer.WriteNumber("count", block.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("initialEntityCapacity", settings.InitialEntityCapacity);
        writer.WriteNumber("maxOversizePercent", settings.MaxOversizePercent);
        writer.WriteNumber("releaseBatchSize", settings.ReleaseBatchSize);
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, ProfileEvidence evidence)
    {
        writer.WriteStartObject("evidence");
        writer.WriteString("verifierVersion", evidence.VerifierVersion);
        writer.WriteString("corpusSha256", evidence.CorpusSha256);
        writer.WriteString("verificationSha256", evidence.VerificationSha256);
        writer.WriteNumber("trainingScenarios", evidence.TrainingScenarios);
        writer.WriteNumber("holdoutScenarios", evidence.HoldoutScenarios);
        writer.WriteNumber("repetitions", evidence.Repetitions);
        writer.WriteEndObject();
    }
}
