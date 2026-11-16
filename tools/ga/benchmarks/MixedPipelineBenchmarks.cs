using BenchmarkDotNet.Attributes;
using SKECS.GA.Backend.Vulkan;

namespace SKECS.GA.Benchmarks;

[MemoryDiagnoser]
public class MixedPipelineBenchmarks
{
    private PipelineWorkload _workload = null!;
    private uint[]? _lastCpu;
    private GaPackedDispatchResult? _lastVulkan;

    internal static PipelineTarget Target { get; set; } = null!;
    internal static int[] RowCounts { get; set; } = [];

    public IEnumerable<int> RowInputs => RowCounts;

    [ParamsSource(nameof(RowInputs))]
    public int Rows { get; set; }

    [GlobalSetup]
    public void SetUp() => _workload = new PipelineWorkload(Target, Rows);

    [Benchmark(Baseline = true)]
    public void CpuStagedCSharp() => _lastCpu = _workload.ExecuteCpu();

    [Benchmark]
    public void VulkanSubmitWaitReadback() => _lastVulkan = _workload.ExecuteVulkan();

    [GlobalCleanup]
    public void ValidateMeasuredResults()
    {
        if (_lastCpu != null)
            _workload.ValidateCpu(_lastCpu);
        if (_lastVulkan != null)
            _workload.ValidateVulkan(_lastVulkan);
    }
}
