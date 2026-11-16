namespace SKECS.GA;

internal static class GaFixedSlangLibrary
{
    internal const int NumericContractVersion = 1;

    internal static string Load()
    {
        using var stream = typeof(GaFixedSlangLibrary).Assembly.GetManifestResourceStream("SKECS.GA.GaFixedV1.slang")
            ?? throw new InvalidOperationException("Missing Slang implementation of the Fixed numeric contract.");
        using var source = new StreamReader(stream);
        return source.ReadToEnd().ReplaceLineEndings("\n");
    }
}
