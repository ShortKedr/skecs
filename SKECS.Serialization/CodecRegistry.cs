namespace SKECS.Serialization;

/// <summary>The component fields for one explicit, versioned migration step.</summary>
public sealed class MigrationFields
{
    private readonly Dictionary<uint, FieldValue> _fields = new();

    public bool TryGet(uint tag, out FieldValue value) => _fields.TryGetValue(tag, out value);
    public FieldValue Get(uint tag) => TryGet(tag, out var value)
        ? value : throw new InvalidDataException($"Missing migration field {tag}.");
    public void Set(uint tag, FieldValue value)
    {
        if (tag == 0 || value.Kind == 0) throw new ArgumentException("A tag and value are required.");
        _fields[tag] = value;
    }
    public bool Remove(uint tag) => _fields.Remove(tag);
    internal void Clear() => _fields.Clear();
    internal IEnumerable<KeyValuePair<uint, FieldValue>> Fields => _fields;
}

/// <summary>The stable schema and typed codecs used by checkpoint and comparison consumers.</summary>
public sealed class CodecRegistry
{
    private readonly SortedDictionary<uint, Codec> _byId = new();
    private readonly Dictionary<Type, Codec> _byType = new();
    private readonly Dictionary<(uint, uint), MigrationStep> _migrations = new();
    private readonly Dictionary<string, CheckpointUpgrade> _checkpointMigrations = new();
    private readonly object _sync = new();
    private bool _sealed;

    public CodecRegistry Register<T>(ComponentSchema schema, IComponentCodec<T> codec) where T : unmanaged
    {
        if (schema is null) throw new ArgumentNullException(nameof(schema));
        if (codec is null) throw new ArgumentNullException(nameof(codec));
        lock (_sync)
        {
            EnsureMutable();
            if (_byId.ContainsKey(schema.Id) || _byType.ContainsKey(typeof(T)))
                throw new ArgumentException($"Component ID {schema.Id} or type {typeof(T)} is already registered.");
            var entry = new Codec<T>(schema, codec);
            _byId.Add(schema.Id, entry);
            _byType.Add(typeof(T), entry);
        }
        return this;
    }

    public CodecRegistry Migrate(uint componentId, uint fromVersion, uint toVersion, Action<MigrationFields> migrate)
    {
        if (componentId == 0 || fromVersion == 0 || toVersion <= fromVersion)
            throw new ArgumentException("Migrations require a positive component ID and increasing versions.");
        if (migrate is null) throw new ArgumentNullException(nameof(migrate));
        lock (_sync)
        {
            EnsureMutable();
            _migrations.Add((componentId, fromVersion), new MigrationStep(toVersion, migrate));
        }
        return this;
    }

    /// <summary>Registers a structural migration between two exact application schema fingerprints.</summary>
    public CodecRegistry MigrateCheckpoint(byte[] fromFingerprint, byte[] toFingerprint, Action<LogicalCheckpoint> migrate)
    {
        var from = FingerprintKey(fromFingerprint);
        var to = FingerprintKey(toFingerprint);
        if (from == to) throw new ArgumentException("A structural migration must change the schema fingerprint.");
        if (migrate is null) throw new ArgumentNullException(nameof(migrate));
        lock (_sync)
        {
            EnsureMutable();
            _checkpointMigrations.Add(from, new CheckpointUpgrade(to, migrate));
        }
        return this;
    }

    public IReadOnlyList<ComponentSchema> GetSchemas()
    {
        Seal();
        return _byId.Values.Select(codec => codec.Schema).ToArray();
    }

    public ComponentSchema GetSchema(Type componentType)
    {
        Seal();
        return _byType[componentType].Schema;
    }

    public byte[] GetFingerprint()
    {
        lock (_sync) return CheckpointFormat.Fingerprint(_byId.Values.Select(codec => codec.Schema));
    }

    /// <summary>Visits a component by stable field tag using its checkpoint codec.</summary>
    /// <remarks>Use at a completed boundary. The callback must not change the world.</remarks>
    public void VisitFields(World world, Entity entity, uint componentId, Action<FieldSchema, FieldValue> visit)
    {
        if (visit is null) throw new ArgumentNullException(nameof(visit));
        Seal();
        var codec = _byId[componentId];
        var values = new FieldValue[codec.Fields.Length];
        var limits = new CheckpointLimits();
        var fields = new ComponentWriter(world, codec.Fields, values, limits.MaxValueBytes);
        fields.StartBlock(limits.MaxFrameBytes);
        codec.Capture(world.OpenCheckpoint(), entity, fields);
        fields.Complete();
        for (var i = 0; i < values.Length; i++) visit(codec.Fields[i], values[i]);
    }

    private void EnsureMutable()
    {
        if (_sealed) throw new InvalidOperationException("The codec registry is already in use.");
    }

    internal void Seal()
    {
        lock (_sync) _sealed = true;
    }

    internal bool TryGet(uint id, out Codec? codec) => _byId.TryGetValue(id, out codec);
    internal bool TryGet(Type type, out Codec? codec) => _byType.TryGetValue(type, out codec);

    internal Action<MigrationFields>[] GetMigrationPath(ComponentSchema source, ComponentSchema target)
    {
        var path = new List<Action<MigrationFields>>();
        var version = source.Version;
        while (version < target.Version && _migrations.TryGetValue((source.Id, version), out var step))
        {
            path.Add(step.Apply);
            version = step.NextVersion;
        }
        if (version != target.Version)
            throw new InvalidDataException($"No complete migration for component {source.Id}, {source.Version} to {target.Version}.");
        return path.ToArray();
    }

    internal Action<LogicalCheckpoint>[] GetCheckpointMigrationPath(byte[] sourceFingerprint)
    {
        if (_checkpointMigrations.Count == 0) return Array.Empty<Action<LogicalCheckpoint>>();
        var source = FingerprintKey(sourceFingerprint);
        var target = FingerprintKey(GetFingerprint());
        if (source == target || !_checkpointMigrations.ContainsKey(source)) return Array.Empty<Action<LogicalCheckpoint>>();

        var seen = new HashSet<string>();
        var path = new List<Action<LogicalCheckpoint>>();
        while (source != target)
        {
            if (!seen.Add(source) || !_checkpointMigrations.TryGetValue(source, out var step))
                throw new InvalidDataException("No complete structural checkpoint migration to the registered schema.");
            path.Add(step.Apply);
            source = step.Next;
        }
        return path.ToArray();
    }

    private static string FingerprintKey(byte[] fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != 32)
            throw new ArgumentException("Expected a 32-byte SHA-256 schema fingerprint.", nameof(fingerprint));
        return Convert.ToBase64String(fingerprint);
    }

    private readonly struct CheckpointUpgrade
    {
        internal CheckpointUpgrade(string next, Action<LogicalCheckpoint> apply) { Next = next; Apply = apply; }
        internal string Next { get; }
        internal Action<LogicalCheckpoint> Apply { get; }
    }

    private readonly struct MigrationStep
    {
        internal MigrationStep(uint nextVersion, Action<MigrationFields> apply)
        {
            NextVersion = nextVersion;
            Apply = apply;
        }

        internal uint NextVersion { get; }
        internal Action<MigrationFields> Apply { get; }
    }
}

internal abstract class Codec
{
    protected Codec(ComponentSchema schema) { Schema = schema; Fields = CodecFields.Active(schema); }
    internal ComponentSchema Schema { get; }
    internal FieldSchema[] Fields { get; }
    internal abstract void Capture(World.CheckpointView world, Entity entity, ComponentWriter fields);
    internal abstract void Restore(World world, Entity entity, ComponentReader fields, ref long remainingBytes);
}

internal sealed class Codec<T> : Codec where T : unmanaged
{
    private readonly IComponentCodec<T> _codec;

    internal Codec(ComponentSchema schema, IComponentCodec<T> codec) : base(schema) => _codec = codec;

    internal override void Capture(World.CheckpointView world, Entity entity, ComponentWriter fields)
    {
        if (!world.TryGet<T>(entity, out var component))
            throw new InvalidOperationException($"Component {Schema.Id} is missing on {entity}.");
        _codec.Write(component, fields);
    }

    internal override void Restore(World world, Entity entity, ComponentReader fields, ref long remainingBytes)
    {
        var component = _codec.Read(fields);
        try
        {
            if (!world.TryAddCheckpointComponent(entity, component, ref remainingBytes))
                throw new InvalidDataException("Checkpoint components exceed the estimated world storage budget.");
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Checkpoint components exceed the world storage layout limits.", error);
        }
    }
}
