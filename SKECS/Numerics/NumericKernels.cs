using System;
#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace SKECS.Numerics;

public static class NumericKernels
{
    /// <summary>Adds two equally sized input spans into an equally sized destination using unchecked Int32 wraparound.</summary>
    /// <remarks>The destination may be disjoint from or exactly coincide with either input.
    /// Partial overlap with either input is rejected before writing. The inputs may overlap one another.
    /// SIMD requires no vector alignment. The .NET Standard targets always use the scalar implementation.</remarks>
    /// <exception cref="ArgumentException">Lengths differ or the destination partially overlaps an input.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mode is invalid.</exception>
    public static void AddInt32(ReadOnlySpan<int> left, ReadOnlySpan<int> right, Span<int> destination,
        KernelMode mode = KernelMode.Auto)
    {
        if (right.Length != left.Length)
            throw new ArgumentException("Both inputs must have the same length.", nameof(right));
        if (destination.Length != left.Length)
            throw new ArgumentException("The destination must have the same length as the inputs.", nameof(destination));
        if (mode != KernelMode.Auto && mode != KernelMode.Scalar)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if ((left.Overlaps(destination, out var leftOffset) && leftOffset != 0) ||
            (right.Overlaps(destination, out var rightOffset) && rightOffset != 0))
            throw new ArgumentException("The destination may only overlap an input exactly.", nameof(destination));

        var length = destination.Length;
        var i = 0;
#if NET8_0_OR_GREATER
        if (mode == KernelMode.Auto)
        {
            ref var leftStart = ref MemoryMarshal.GetReference(left);
            ref var rightStart = ref MemoryMarshal.GetReference(right);
            ref var destinationStart = ref MemoryMarshal.GetReference(destination);
            if (Vector.IsHardwareAccelerated && Vector<int>.Count > Vector128<int>.Count && length >= Vector<int>.Count)
            {
                for (; i <= length - Vector<int>.Count; i += Vector<int>.Count)
                {
                    var a = Unsafe.ReadUnaligned<Vector<int>>(ref ByteAt(ref leftStart, i));
                    var b = Unsafe.ReadUnaligned<Vector<int>>(ref ByteAt(ref rightStart, i));
                    Unsafe.WriteUnaligned(ref ByteAt(ref destinationStart, i), a + b);
                }
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                for (; i <= length - Vector128<int>.Count; i += Vector128<int>.Count)
                {
                    var a = Unsafe.ReadUnaligned<Vector128<int>>(ref ByteAt(ref leftStart, i));
                    var b = Unsafe.ReadUnaligned<Vector128<int>>(ref ByteAt(ref rightStart, i));
                    Unsafe.WriteUnaligned(ref ByteAt(ref destinationStart, i), a + b);
                }
            }
        }
#endif
        for (; i < length; i++)
            destination[i] = unchecked(left[i] + right[i]);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte ByteAt(ref int start, int index) =>
        ref Unsafe.As<int, byte>(ref Unsafe.Add(ref start, index));
#endif
}
