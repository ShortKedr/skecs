namespace SKECS.GA;

[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class SecsSystemAttribute : Attribute
{
    public string? SystemId { get; set; }
}
