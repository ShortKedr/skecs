namespace SKECS.GA;

internal static class GaFieldPath
{
    internal static string CSharpAccess(string root, string path, string? index = null)
    {
        string suffix = string.Join(".", path.Split('.').Select(segment => segment[0] == '[' ? segment : "@" + segment));
        if (index is not null)
            suffix = suffix.Replace("[0]", $"[{index}]", StringComparison.Ordinal);
        return path[0] == '[' ? root + suffix : root + "." + suffix;
    }
}
