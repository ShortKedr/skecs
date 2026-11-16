namespace SKECS.GA;

/// <summary>Declares the readonly flat buffer element type used by the kernel.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GaReadBufferAttribute(Type elementType) : Attribute
{
    public Type ElementType { get; } = elementType;
}
