namespace SKECS.GA;

internal static class GaEnum
{
    internal static bool IsDefined<T>(T value) where T : struct, Enum
    {
#if NET8_0_OR_GREATER
        return Enum.IsDefined(value);
#else
        return Enum.IsDefined(typeof(T), value);
#endif
    }
}
