namespace SKECS.Serialization;

/// <summary>Maps a component to and from its explicitly registered logical fields.</summary>
public interface IComponentCodec<T> where T : unmanaged
{
    void Write(in T value, ComponentWriter fields);
    T Read(ComponentReader fields);
}

public sealed class ComponentWriter
{
    private readonly World _world;
    private readonly FieldSchema[] _fields;
    private readonly FieldValue[] _values;
    private readonly int _maxValueBytes;
    private int _offset;
    private long _remainingBytes;

    internal ComponentWriter(World world, FieldSchema[] fields, FieldValue[] values, int maxValueBytes)
    {
        _world = world;
        _fields = fields;
        _values = values;
        _maxValueBytes = maxValueBytes;
    }

    internal void Start(int offset) => _offset = offset;
    internal void StartBlock(long remainingBytes) => _remainingBytes = remainingBytes;

    public void Set(uint tag, FieldValue value)
    {
        var index = CodecFields.Find(_fields, tag);
        if (index < 0 || value.Kind != _fields[index].Kind)
            throw new InvalidOperationException($"Field {tag} has the wrong kind or is absent from the schema.");
        if (_values[_offset + index].Kind != 0)
            throw new InvalidOperationException($"Field {tag} was written twice.");
        if (value.Kind == FieldKind.Bytes)
        {
            var bytes = value.AsBytes();
            if (bytes.Length > _maxValueBytes) throw new InvalidDataException($"Field {tag} exceeds the value limit.");
            _remainingBytes -= bytes.Length + 32L;
            if (_remainingBytes < 0) throw new InvalidDataException("Checkpoint block exceeds the available working storage.");
            value = FieldValue.FromBytes((byte[])bytes.Clone());
        }
        if (value.Kind == FieldKind.Entity) CodecFields.ValidateEntity(_world, value);
        _values[_offset + index] = value;
    }

    public void SetEntity(uint tag, Entity entity)
    {
        var bound = entity != new Entity(entity.Id, entity.Version);
        if (bound && entity != _world.BindEntity(entity.Id, entity.Version))
            throw new InvalidDataException($"Entity field {tag} belongs to a different world.");
        Set(tag, FieldValue.FromEntity(entity.Id, entity.Version, bound));
    }

    internal void Complete()
    {
        for (var index = 0; index < _fields.Length; index++)
            if (_values[_offset + index].Kind == 0)
                throw new InvalidOperationException($"Codec did not write field {_fields[index].Tag}.");
    }
}

public sealed class ComponentReader
{
    private readonly World _world;
    private readonly FieldSchema[] _fields;
    private readonly FieldValue[] _values;

    internal ComponentReader(World world, FieldSchema[] fields, FieldValue[] values)
    {
        _world = world;
        _fields = fields;
        _values = values;
    }

    public bool TryGet(uint tag, out FieldValue value)
    {
        var index = CodecFields.Find(_fields, tag);
        value = index < 0 ? default : _values[index];
        return value.Kind != 0;
    }

    public FieldValue Get(uint tag) => TryGet(tag, out var value)
        ? value : throw new InvalidDataException($"Missing field {tag}.");

    public Entity GetEntity(uint tag)
    {
        Get(tag).AsEntity(out var id, out var version, out var bound);
        return bound ? _world.BindEntity(id, version) : new Entity(id, version);
    }
}

internal static class CodecFields
{
    public static FieldSchema[] Active(ComponentSchema schema) => schema.Fields.Where(field => !field.Retired).ToArray();

    public static int Find(FieldSchema[] fields, uint tag)
    {
        var first = 0;
        var last = fields.Length - 1;
        while (first <= last)
        {
            var index = first + (last - first) / 2;
            var candidate = fields[index].Tag;
            if (candidate == tag) return index;
            if (candidate < tag) first = index + 1;
            else last = index - 1;
        }
        return -1;
    }

    public static void ValidateEntity(World world, FieldValue value)
    {
        value.AsEntity(out var id, out var version, out var bound);
        if (!bound) return;
        var view = world.OpenCheckpoint();
        if ((uint)id >= (uint)view.SlotCount || version <= 0 || version > view.GetSlot(id).Version)
            throw new InvalidDataException($"Entity reference {id}:{version} was never issued by this world.");
    }

    public static void ValidateEntity(ReadOnlySpan<CheckpointSlot> slots, FieldValue value)
    {
        value.AsEntity(out var id, out var version, out var bound);
        if (bound && ((uint)id >= (uint)slots.Length || version <= 0 || version > slots[id].Version))
            throw new InvalidDataException($"Entity reference {id}:{version} was never issued by this checkpoint.");
    }
}
