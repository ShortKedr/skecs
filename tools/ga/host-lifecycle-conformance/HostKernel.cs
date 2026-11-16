using SKECS.GA;
using SKECS.Numerics;

public struct HostInput
{
    public int Divisor;
}

public struct HostBody
{
    public int Index;
    public Fixed Position;
}

[SecsSystem(SystemId = "host-lifecycle")]
public partial struct HostKernel
{
    [GaTickResource(typeof(HostInput))]
    public void Execute(in SystemContext context, ref HostBody body)
    {
        ref readonly var input = ref context.Resource<HostInput>();
        int divisor = body.Index >= 64 && body.Index <= 65 ? input.Divisor : 1;
        body.Position += Fixed.FromInt32(body.Index % 7 - 3) / Fixed.FromInt32(divisor);
        context.Emit(body.Index);
    }
}
