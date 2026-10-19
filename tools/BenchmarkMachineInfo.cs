using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

public static class BenchmarkMachineInfo
{
    public static string CpuName
    {
        get
        {
            if (!X86Base.IsSupported || unchecked((uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax) < 0x80000004)
                return RuntimeInformation.ProcessArchitecture.ToString();
            var bytes = new byte[48];
            for (int i = 0; i < 3; i++)
            {
                var registers = X86Base.CpuId(unchecked((int)0x80000002) + i, 0);
                int[] values = [registers.Eax, registers.Ebx, registers.Ecx, registers.Edx];
                for (int j = 0; j < 4; j++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 16 + j * 4, 4), values[j]);
            }
            return Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
        }
    }
}
