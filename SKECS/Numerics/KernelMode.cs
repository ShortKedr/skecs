namespace SKECS.Numerics;

/// <summary>Selects the execution mode for a numerical kernel.</summary>
public enum KernelMode
{
    /// <summary>Use SIMD when supported by the runtime and input length.</summary>
    Auto,
    /// <summary>Use the scalar implementation.</summary>
    Scalar
}
