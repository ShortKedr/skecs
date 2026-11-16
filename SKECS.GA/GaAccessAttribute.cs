namespace SKECS.GA;

/// <summary>Declares how a kernel accesses an extra component on its current entity.</summary>
public enum GaOwnAccess
{
    Read,
    ReadWrite,
    OptionalRead
}

/// <summary>Declares component access independently of query membership.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class GaAccessAttribute(Type component, GaOwnAccess access) : Attribute
{
    public Type Component { get; } = component;
    public GaOwnAccess Access { get; } = access;
}
