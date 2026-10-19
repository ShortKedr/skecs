using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace SKECS.BenchmarkTools;

public static class HardwareCapture
{
    public static JsonObject Create() => new()
    {
        ["Machine"] = BenchmarkMachineLabel.Value, ["CapturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        ["Cpu"] = new JsonArray(new JsonObject { ["Name"] = Workspace.CpuName(), ["NumberOfLogicalProcessors"] = Environment.ProcessorCount }),
        ["Os"] = RuntimeInformation.OSDescription, ["OsVersion"] = Environment.OSVersion.Version.ToString(),
        ["AvailableMemoryBytes"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        ["MemoryKind"] = "GC total available memory limit; not physical RAM"
    };
}
