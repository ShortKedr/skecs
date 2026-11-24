namespace SKECS.GA;

/// <summary>Kernel and property names for Unity compute shader artifacts.</summary>
public static class GaUnityComputeAbi
{
    public const string MainKernel = "main";
    public const string ProbeKernel = "gaStartupProbe";
    public const string InputBuffer = "gaInput";
    public const string OutputBuffer = "gaOutput";
    public const string FaultBuffer = "gaFaults";
    public const string InputWords = "gaInputWords";
    public const string OutputWords = "gaOutputWords";
    public const string FaultWords = "gaFaultWords";
    public const int WordStride = 4;
    public const string TargetProfile = "unity_compute";
    public const string CodegenOption = "SKECS.GA Unity Compute codegen=1";
}
