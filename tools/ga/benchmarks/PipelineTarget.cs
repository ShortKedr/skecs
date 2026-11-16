using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;

namespace SKECS.GA.Benchmarks;

internal sealed class PipelineTarget : IDisposable
{
    private readonly GaVulkanBackend _backend;
    private readonly GaStartupResult _startup;

    private PipelineTarget(GaVulkanBackend backend, GaStartupResult startup, GaProgram program, GaVulkanKernel kernel)
    {
        _backend = backend;
        _startup = startup;
        Program = program;
        Kernel = kernel;
    }

    internal GaProgram Program { get; }
    internal GaVulkanKernel Kernel { get; }
    internal GaDeviceInfo Device => _startup.Decisions[0].Device!;
    internal string ComputeProfile => _startup.Decisions[0].ComputeProfile!;
    internal GaQualification Qualification => _startup.Decisions[0].Qualification!.Value;
    internal string ArtifactId => _startup.Decisions[0].ArtifactId!;

    internal static async Task<PipelineTarget> Create(string nativeDirectory, string directory, int deviceIndex)
    {
        string name = await RequireHardware(Path.Combine(nativeDirectory, "ga-vulkan.exe"), deviceIndex);
        var program = CompileKernel();
        var bundle = await GaVulkanPackager.Build(program, "benchmarks.mixed-movement/1",
            Path.Combine(nativeDirectory, "ga-slang-probe.exe"), directory, "mixed-movement");
        var backend = GaVulkanBackend.Open(Path.Combine(nativeDirectory, "ga-vulkan-native.dll"));
        GaStartupResult? startup = null;
        try
        {
            if ((uint)deviceIndex >= (uint)backend.Devices.Count ||
                !backend.Devices[deviceIndex].Info.Device.StartsWith(name + " (", StringComparison.Ordinal))
                throw new InvalidDataException("The managed Vulkan device listing changed since hardware validation.");

            var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
            startup = GaStartup.Select(GaPolicy.RequireGpu, [request], [backend.Devices[deviceIndex]], allowExperimental: true);
            if (!startup.Succeeded || startup.GetPreparedKernel(program.SystemId) is not GaVulkanKernel kernel)
                throw new InvalidOperationException($"RequireGpu failed for Vulkan device {deviceIndex}. {startup.Decisions[0]}");
            return new PipelineTarget(backend, startup, program, kernel);
        }
        catch
        {
            try { startup?.Dispose(); }
            finally { backend.Dispose(); }
            throw;
        }
    }

    private static GaProgram CompileKernel()
    {
        using var source = typeof(MixedMovement).Assembly.GetManifestResourceStream("Ga.Benchmarks.MixedMovement.cs")!;
        using var reader = new StreamReader(source);
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("GaBenchmarkKernel",
            [CSharpSyntaxTree.ParseText(reader.ReadToEnd(), path: "MixedMovement.cs")],
            new[] { typeof(object).Assembly.Location, runtime, typeof(Fixed).Assembly.Location, typeof(SecsSystemAttribute).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = compilation.GetTypeByMetadataName(typeof(MixedMovement).FullName!)!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(compilation, entry);
        return result.Program ?? throw new InvalidDataException(string.Join("\n", result.Diagnostics));
    }

    private static async Task<string> RequireHardware(string vulkanProgram, int deviceIndex)
    {
        var start = new ProcessStartInfo(vulkanProgram)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("devices");
        using var process = Process.Start(start) ?? throw new IOException("Unable to start the local Vulkan device probe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Vulkan device enumeration failed. {await errors}");

        using var reader = new StringReader(await output);
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split('\t');
            if (fields is [var index, _, _, _, _, var type, var name] &&
                index == deviceIndex.ToString(CultureInfo.InvariantCulture) && type is "1" or "2")
                return name;
        }
        throw new ArgumentException($"Vulkan device {deviceIndex} is not a reported integrated or discrete GPU.", nameof(deviceIndex));
    }

    public void Dispose()
    {
        try { _startup.Dispose(); }
        finally { _backend.Dispose(); }
    }
}
