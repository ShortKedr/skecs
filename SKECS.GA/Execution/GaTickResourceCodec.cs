namespace SKECS.GA;

/// <summary>Packs an immutable runner input into the resource words declared by a kernel.</summary>
public delegate void GaPackTickResource<TInput>(in TInput input, Span<uint> words) where TInput : unmanaged;

/// <summary>The generated packing contract for a kernel's frozen tick input.</summary>
public readonly record struct GaTickResourceCodec<TInput>(
    string TypeId, int WordCount, GaPackTickResource<TInput> Pack) where TInput : unmanaged;
