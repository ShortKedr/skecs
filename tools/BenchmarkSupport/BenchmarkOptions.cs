namespace SKECS.BenchmarkTools;

public sealed record BenchmarkOptions(string Command, string Suite,
    Dictionary<string, string> Values, HashSet<string> Flags)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        string command = args.Length == 0 ? "help" : args[0];
        bool hasSuite = args.Length > 1 && !args[1].StartsWith('-');
        string suite = hasSuite ? args[1] : "all";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = hasSuite ? 2 : 1; i < args.Length; i++)
        {
            if (args[i] is "--check" or "--no-build" or "--include-desktop" or "--skip-ticks") flags.Add(args[i]);
            else if (args[i] is "--root" or "--output" or "--data" or "--ticks" or "--warmup")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value: " + args[i]);
                if (!values.TryAdd(args[i], args[++i])) throw new ArgumentException("Repeated option: " + args[i - 1]);
            }
            else throw new ArgumentException("Unknown option: " + args[i]);
        }
        return new(command, suite, values, flags);
    }
}
