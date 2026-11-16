using System.ComponentModel;
using System.Text.Json;
using SKECS.RuntimeProfiles.Tools;

try
{
    switch (args)
    {
        case ["capture", var corpus, var runDirectory]:
            new TuningSession(Path.GetFullPath(runDirectory)).Capture(Path.GetFullPath(corpus));
            return 0;
        case ["propose", var runDirectory]:
            return new TuningSession(Path.GetFullPath(runDirectory)).Propose();
        case ["verify", var runDirectory]:
            return new TuningSession(Path.GetFullPath(runDirectory)).Verify();
        case ["emit", var runDirectory, var outputDirectory]:
            new TuningSession(Path.GetFullPath(runDirectory)).Emit(Path.GetFullPath(outputDirectory));
            return 0;
        case ["inspect", var tracePath]:
            using (var stream = File.OpenRead(tracePath))
            {
                var trace = TraceReader.Read(stream);
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(Artifacts.Serialize(new
                {
                    trace.Header, trace.ChunkCapacity, trace.Frames, trace.LowerBound, trace.PeakRetained
                })));
            }
            return 0;
        case ["__run", var requestPath, var resultPath]:
            Artifacts.Write(resultPath, ScenarioRunner.Run(Artifacts.Read<RunRequest>(requestPath)));
            return 0;
        default:
            Console.Error.WriteLine("Usage: capture <corpus.json> <new-run-dir> | propose <run-dir> | verify <run-dir> | emit <run-dir> <new-output-dir> | inspect <trace.xml>");
            return 2;
    }
}
catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or
    JsonException or ArgumentException or InvalidOperationException or OverflowException or Win32Exception)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
