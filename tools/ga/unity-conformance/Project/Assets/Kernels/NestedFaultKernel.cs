using SKECS.GA;

public struct NestedFaultRow
{
    public int Divisor;
    public int Result;
}

[SecsSystem(SystemId = "unity.nested-fault")]
public partial struct NestedFaultKernel
{
    public void Execute(in SystemContext context, ref NestedFaultRow row)
    {
        for (int outer = 0; outer < 2; ++outer)
        {
            for (int inner = 0; inner < 2; ++inner)
                row.Result = checked(row.Result + 17 / (row.Divisor - outer - inner));
        }
    }
}
