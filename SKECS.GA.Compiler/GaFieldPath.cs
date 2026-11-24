namespace SKECS.GA;

internal static class GaFieldPath
{
    internal static string CSharpAssignment(string field, string value, bool isReadOnly, bool unityCompatible)
    {
        if (isReadOnly)
        {
            // The typed row is a writable local copy. Whole-value replacement can change readonly leaves.
            const string marshal = "global::System.Runtime.InteropServices.MemoryMarshal";
            field = unityCompatible ? $"global::System.Runtime.CompilerServices.Unsafe.AsRef(in {field})" :
                $"{marshal}.GetReference({marshal}.CreateReadOnlySpan(in {field}, 1))";
        }
        return $"{field} = {value};";
    }

    internal static string CSharpAccess(string root, GaField field, string? index = null) =>
        CSharpAccess(root, field.Path, index, field.FixedArrayType, writable: field.Access == GaFieldAccess.ReadWrite);

    internal static string CSharpAccess(string root, string path, string? index = null, string? fixedArrayType = null,
        bool writable = false)
    {
        if (fixedArrayType is not null)
        {
            int start = path.IndexOf('[');
            int end = path.IndexOf(']', start);
            string storage = start == 0 ? root : CSharpAccess(root, path[..start]);
            string elementIndex = index ?? path[(start + 1)..end];
            string accessor = writable ? "At(ref " : "Read(in ";
            string element = $"{fixedArrayType}.{accessor}{storage}, {elementIndex})";
            return end + 1 == path.Length ? element : CSharpAccess(element, path[(end + 2)..]);
        }
        string suffix = string.Join(".", path.Split('.').Select(segment => segment[0] == '[' ? segment : "@" + segment));
        if (index is not null)
            suffix = suffix.Replace("[0]", $"[{index}]", StringComparison.Ordinal);
        return path[0] == '[' ? root + suffix : root + "." + suffix;
    }
}
