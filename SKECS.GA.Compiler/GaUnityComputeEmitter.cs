namespace SKECS.GA;

public sealed record GaUnityComputeModule(string Source, IReadOnlyList<GaSourceSpan> FaultSources, int WorkgroupX);

/// <summary>Emits Unity-importable HLSL directly from a verified GA program.</summary>
public static class GaUnityComputeEmitter
{
    // OpenGL ES 3.1, table 20.45, guarantees 128 invocations in a workgroup.
    public const int PortableWorkgroupX = 128;
    public const int MaximumWorkgroupX = GaSlangEmitter.MaximumD3D12WorkgroupX;

    public static GaUnityComputeModule Emit(GaProgram program, int workgroupX = GaSlangModule.ThreadsPerGroup)
    {
        if (workgroupX < 1 || workgroupX > MaximumWorkgroupX)
            throw new ArgumentOutOfRangeException(nameof(workgroupX));
        var module = GaSlangEmitter.EmitUnityCompute(program, workgroupX);
        return new GaUnityComputeModule(module.Source, module.FaultSources, workgroupX);
    }
}
