using System;
using System.Collections.Generic;

namespace SKECS;

/// <summary>Stable logical field types shared by checkpoint and comparison consumers.</summary>
public enum FieldKind : byte
{
    Boolean = 1, Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64,
    Float32, Float64, Entity, Bytes
}

public readonly struct FieldSchema
{
    public FieldSchema(uint tag, FieldKind kind, bool optional = false, bool retired = false)
    {
        if (tag == 0) throw new ArgumentOutOfRangeException(nameof(tag));
        if (kind < FieldKind.Boolean || kind > FieldKind.Bytes) throw new ArgumentOutOfRangeException(nameof(kind));
        Tag = tag;
        Kind = kind;
        Optional = optional;
        Retired = retired;
    }

    public uint Tag { get; }
    public FieldKind Kind { get; }
    public bool Optional { get; }
    public bool Retired { get; }
}

/// <summary>An immutable schema identified by application-assigned IDs and field tags.</summary>
public sealed class ComponentSchema
{
    public ComponentSchema(uint id, uint version, params FieldSchema[] fields)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (fields is null) throw new ArgumentNullException(nameof(fields));

        Id = id;
        Version = version;
        var sorted = (FieldSchema[])fields.Clone();
        Array.Sort(sorted, (left, right) => left.Tag.CompareTo(right.Tag));
        uint last = 0;
        foreach (var field in sorted)
        {
            if (field.Tag <= last) throw new ArgumentException("Field tags must be positive and unique.", nameof(fields));
            last = field.Tag;
        }
        Fields = Array.AsReadOnly(sorted);
    }

    public uint Id { get; }
    public uint Version { get; }
    public IReadOnlyList<FieldSchema> Fields { get; }
}
