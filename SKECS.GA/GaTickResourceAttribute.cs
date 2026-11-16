namespace SKECS.GA;

/// <summary>Declares the frozen runner input type used by the kernel for each tick.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GaTickResourceAttribute(Type inputType) : Attribute
{
    public Type InputType { get; } = inputType;
}
