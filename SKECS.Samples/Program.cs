using Samples = Skecs.Samples;

var samples = new (string Name, Func<string[], Task> Run)[]
{
    ("core", Samples.Core.Sample.RunAsync),
    ("serialization", Samples.Serialization.Sample.RunAsync),
    ("network", Samples.Network.Sample.RunAsync),
    ("ga", Samples.Ga.Sample.RunAsync),
    ("profiler", Samples.Profiler.Sample.RunAsync),
    ("runtime-profiles", Samples.RuntimeProfiles.Sample.RunAsync),
    ("determinism", Samples.Determinism.Sample.RunAsync),
    ("agentic", Samples.Agentic.Sample.RunAsync),
};

if (args is [] or ["--help" or "-h" or "list"])
{
    Console.WriteLine("Usage: dotnet run --project SKECS.Samples -c Release -- <sample> [options]");
    Console.WriteLine("Samples:");
    foreach (var sample in samples)
        Console.WriteLine($"  {sample.Name}");
    return 0;
}

var selected = Array.FindIndex(samples, sample => sample.Name == args[0]);
if (selected < 0)
{
    Console.Error.WriteLine($"Unknown sample '{args[0]}'. Use --help to list the samples.");
    return 2;
}

try
{
    await samples[selected].Run(args[1..]);
    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine($"{samples[selected].Name}: {error.Message}");
    return 1;
}
