namespace SKECS.GA;

internal static class GaCompilerText
{
    internal static string NormalizeLineEndings(string text)
    {
#if NETSTANDARD2_1
        // Keep source digests identical to String.ReplaceLineEndings on .NET 8.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\f', '\n')
            .Replace('\u0085', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n');
#else
        return text.ReplaceLineEndings("\n");
#endif
    }
}
