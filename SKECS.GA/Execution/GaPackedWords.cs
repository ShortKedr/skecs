namespace SKECS.GA;

internal static class GaPackedWords
{
#if NET8_0_OR_GREATER
    internal static int MaximumLength => Array.MaxLength;
#else
    // Apply the same upper bound as the .NET host to packed GA arrays.
    internal const int MaximumLength = 0x7FFFFFC7;
#endif

    internal static bool HasNonZeroWord(ReadOnlySpan<uint> words)
    {
#if NET8_0_OR_GREATER
        return words.IndexOfAnyExcept(0u) >= 0;
#else
        foreach (uint word in words)
            if (word != 0)
                return true;
        return false;
#endif
    }
}
