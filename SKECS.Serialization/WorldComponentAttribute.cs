namespace SKECS.Serialization;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class WorldComponentAttribute : Attribute
{
    public WorldComponentAttribute(uint id) => Id = id;
    public uint Id { get; }
    public uint Version { get; set; } = 1;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class WorldFieldAttribute : Attribute
{
    public WorldFieldAttribute(uint tag) => Tag = tag;
    public uint Tag { get; }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class WorldIgnoreAttribute : Attribute { }
