namespace Skecs.Migration;

public static class MigrationCli
{
    private const string Usage = "dotnet run --project skecs-migration -- <consumer-directory> --from 0.1.0 --to 0.2.0 [--old-package <path>] [--apply]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h") { output.WriteLine(Usage); return 0; }
        if (args.Length == 0 || args[0].StartsWith('-')) { error.WriteLine(Usage); return 2; }
        string? from = null, to = null, oldPackage = null;
        var apply = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--apply" && !apply) { apply = true; continue; }
            if (args[index] is "--from" or "--to" or "--old-package" && index + 1 < args.Length)
            {
                var option = args[index++];
                if (option == "--from" && from is null) { from = args[index]; continue; }
                if (option == "--to" && to is null) { to = args[index]; continue; }
                if (option == "--old-package" && oldPackage is null) { oldPackage = args[index]; continue; }
            }
            error.WriteLine($"Unknown, duplicate or incomplete option. {Usage}");
            return 2;
        }
        if (from != "0.1.0" || to != "0.2.0")
        {
            error.WriteLine($"Only the prepared 0.1.0 -> 0.2.0 rename is supported. {Usage}");
            return 2;
        }
        try
        {
            var root = Path.GetFullPath(args[0]);
            output.WriteLine("Target: SKECS 0.2.0. Obtain the target package or Unity DLL separately.");
            var changes = Migration.Plan(root, oldPackage, output);
            foreach (var change in changes) PrintDiff(root, change, output);
            if (apply) Migration.Apply(changes);
            output.WriteLine($"{(apply ? "Applied:" : "Dry run:")} {changes.Count} file(s).");
            output.WriteLine("Review shadowed names, inactive preprocessor branches, DLL imports, source project/solution paths, custom assembly references and reflection strings; then restore, build and test the consumer.");
            output.WriteLine("Review world ownership of reconstructed entities, liveness checks, equality and hashing, and any World use after disposal.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                                          ArgumentException or InvalidDataException or BadImageFormatException or System.Xml.XmlException)
        {
            error.WriteLine($"Migration stopped: {exception.Message}");
            return 1;
        }
    }

    private static void PrintDiff(string root, FileChange change, TextWriter output)
    {
        var path = Path.GetRelativePath(root, change.Path).Replace('\\', '/');
        var before = Migration.Decode(change.Original).Text.Split('\n');
        var after = Migration.Decode(change.Updated).Text.Split('\n');
        output.WriteLine($"--- {path}");
        output.WriteLine($"+++ {path}");
        // Renames preserve line count. Merge adjacent three-line context hunks.
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
        for (var position = 0; position < changed.Length; position++)
        {
            var start = Math.Max(0, changed[position] - 3);
            var end = Math.Min(before.Length - 1, changed[position] + 3);
            while (position + 1 < changed.Length && changed[position + 1] - 3 <= end + 1)
                end = Math.Min(before.Length - 1, changed[++position] + 3);
            var count = end - start + 1;
            output.WriteLine($"@@ -{start + 1},{count} +{start + 1},{count} @@");
            for (var index = start; index <= end; index++)
            {
                if (before[index] == after[index]) output.WriteLine(" " + before[index].TrimEnd('\r'));
                else
                {
                    output.WriteLine("-" + before[index].TrimEnd('\r'));
                    output.WriteLine("+" + after[index].TrimEnd('\r'));
                }
            }
        }
    }
}
