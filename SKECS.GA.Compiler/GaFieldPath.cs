namespace SKECS.GA;

internal static class GaFieldPath
{
    internal static string CSharpAssignment(string field, string value, bool isReadOnly)
    {
        if (isReadOnly)
        {
            // The typed row is a writable local copy. Whole-value replacement can change readonly leaves.
            const string marshal = "global::System.Runtime.InteropServices.MemoryMarshal";
            field = $"{marshal}.GetReference({marshal}.CreateReadOnlySpan(in {field}, 1))";
        }
        return $"{field} = {value};";
    }

    internal static string CSharpAccess(string root, string path, string? index = null)
    {
        string suffix = string.Join(".", path.Split('.').Select(segment => segment[0] == '[' ? segment : "@" + segment));
        if (index is not null)
            suffix = suffix.Replace("[0]", $"[{index}]", StringComparison.Ordinal);
        return path[0] == '[' ? root + suffix : root + "." + suffix;
    }
}
