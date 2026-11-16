using System.Globalization;
using System.Numerics;
using SKECS.GA;
using SKECS.Numerics;
using static GaConformanceIo;

internal static class FixedVulkanConformance
{
    private const long Scale = 1L << 32;
    private const long Half = Scale >> 1;
    private enum Operation : uint { Add, Subtract, Negate, Multiply, Divide, FromInt32, ToInt32, Less }
    private readonly record struct Sample(Operation Operation, long Left, long Right);

    private const string Kernel = """
        [[vk::binding(0, 0)]] StructuredBuffer<uint> gaInput;
        [[vk::binding(1, 0)]] RWStructuredBuffer<uint> gaOutput;
        [[vk::binding(2, 0)]] RWStructuredBuffer<uint> gaFaults;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= gaInput[0]) return;
            uint input = 1u + 5u * id.x;
            uint kind = gaInput[input];
            uint2 a = uint2(gaInput[input + 1u], gaInput[input + 2u]);
            uint2 b = uint2(gaInput[input + 3u], gaInput[input + 4u]);
            GaFixedResult result;
            switch (kind)
            {
            case 0u: result = gaFixedAdd(a, b); break;
            case 1u: result = gaFixedSubtract(a, b); break;
            case 2u: result = gaFixedNegate(a); break;
            case 3u: result = gaFixedMultiply(a, b); break;
            case 4u: result = gaFixedDivide(a, b); break;
            case 5u: result = gaFixedResult(gaFixedFromInt32(a.x), 0u); break;
            case 6u:
                uint integer = gaFixedToInt32(a);
                result = gaFixedResult(uint2(integer, integer >= 0x80000000u ? 0xffffffffu : 0u), 0u);
                break;
            default: result = gaFixedResult(uint2(uint(gaFixedLess(a, b)), 0u), 0u); break;
            }
            gaOutput[2u * id.x] = result.bits.x;
            gaOutput[2u * id.x + 1u] = result.bits.y;
            gaFaults[id.x] = result.fault;
        }
        """;

    internal static async Task Compare(string compiler, string validate, string executor, string directory,
        string[] devices)
    {
        if (Fixed.NumericContractVersion != GaFixedSlangLibrary.NumericContractVersion)
            throw new InvalidOperationException("Fixed CPU and GPU numeric contract versions differ.");
        var samples = GetSamples();
        string source = Path.Combine(directory, "fixed.slang");
        string binary = Path.Combine(directory, "fixed.spv");
        string layout = Path.Combine(directory, "fixed.layout");
        await File.WriteAllTextAsync(source, GaFixedSlangLibrary.Load() + "\n" + Kernel);
        await Run(compiler, "compile", source, "main", binary, layout);
        await Run(validate, "--target-env", "vulkan1.1", binary);

        var expected = new (long Raw, GaFaultKind Fault)[samples.Count];
        var input = new uint[1 + samples.Count * 5];
        input[0] = (uint)samples.Count;
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var oracle = Oracle(sample);
            if (Host(sample) != oracle)
                throw new InvalidDataException($"Fixed CPU and BigInteger differ for {sample}.");
            expected[i] = oracle;
            input[1 + i * 5] = (uint)sample.Operation;
            input[2 + i * 5] = unchecked((uint)sample.Left);
            input[3 + i * 5] = unchecked((uint)(sample.Left >> 32));
            input[4 + i * 5] = unchecked((uint)sample.Right);
            input[5 + i * 5] = unchecked((uint)(sample.Right >> 32));
        }

        string inputPath = Path.Combine(directory, "fixed.in");
        Write(inputPath, input);
        foreach (string device in devices)
        {
            string output = Path.Combine(directory, $"fixed-{device}.out");
            string fault = Path.Combine(directory, $"fixed-{device}.fault");
            var gpu = new uint[samples.Count * 2];
            var faults = new uint[samples.Count];
            Write(output, gpu);
            Write(fault, faults);
            var groups = ((samples.Count + 63) / 64).ToString(CultureInfo.InvariantCulture);
            await Run(executor, "dispatch", device, binary, "main", groups,
                "0", "0", "r", inputPath, "0", "1", "rw", output, "0", "2", "rw", fault);
            Read(output, gpu);
            Read(fault, faults);
            for (int i = 0; i < samples.Count; i++)
            {
                long raw = unchecked((long)(((ulong)gpu[2 * i + 1] << 32) | gpu[2 * i]));
                var expectedRow = expected[i];
                if ((GaFaultKind)faults[i] != expectedRow.Fault || expectedRow.Fault == 0 && raw != expectedRow.Raw)
                    throw new InvalidDataException(
                        $"Fixed GPU {device} disagrees for {samples[i]}: expected {expectedRow}, got ({raw}, {faults[i]}).");
            }
            Console.WriteLine($"Vulkan device {device}: {samples.Count} Fixed vectors agree with CPU and BigInteger.");
        }
    }

    private static List<Sample> GetSamples()
    {
        long[] edges = [0, 1, -1, 3, -3, Half, -Half, Scale, -Scale, Scale * 2, -Scale * 2,
            Scale + 1, -(Scale + 1), long.MinValue, long.MinValue + 1, long.MaxValue, long.MaxValue - 1,
            long.MaxValue - Half + 1, -(long.MaxValue - Half + 1)];
        var samples = new List<Sample>();
        foreach (var operation in Enum.GetValues<Operation>())
        {
            foreach (long left in edges)
                foreach (long right in edges)
                    samples.Add(new Sample(operation, left, right));
        }

        var random = new Random(661213);
        for (int i = 0; i < 2049; i++)
        {
            long left = random.NextInt64(long.MinValue, long.MaxValue);
            long right = random.NextInt64(long.MinValue, long.MaxValue);
            // Small magnitudes exercise non-overflowing rounded multiplications and tiny ratios.
            if ((i & 1) == 0)
                left >>= 32;
            samples.Add(new Sample((Operation)(i % 8), left, right));
        }
        return samples;
    }

    private static (long, GaFaultKind) Host(Sample sample)
    {
        var a = Fixed.FromRaw(sample.Left);
        var b = Fixed.FromRaw(sample.Right);
        try
        {
            if (sample.Operation == Operation.Divide && (a / b) != Fixed.FromRatio(sample.Left, sample.Right))
                throw new InvalidDataException("Fixed division and FromRatio use different numeric rules.");
            return (sample.Operation switch
            {
                Operation.Add => (a + b).Raw,
                Operation.Subtract => (a - b).Raw,
                Operation.Negate => (-a).Raw,
                Operation.Multiply => (a * b).Raw,
                Operation.Divide => (a / b).Raw,
                Operation.FromInt32 => Fixed.FromInt32(unchecked((int)sample.Left)).Raw,
                Operation.ToInt32 => (int)a,
                _ => a < b ? 1 : 0
            }, 0);
        }
        catch (OverflowException)
        {
            return (0, GaFaultKind.Overflow);
        }
        catch (DivideByZeroException)
        {
            return (0, GaFaultKind.DivideByZero);
        }
    }

    private static (long, GaFaultKind) Oracle(Sample sample)
    {
        BigInteger a = sample.Left;
        BigInteger b = sample.Right;
        if (sample.Operation == Operation.Divide && b.IsZero)
            return (0, GaFaultKind.DivideByZero);
        BigInteger raw = sample.Operation switch
        {
            Operation.Add => a + b,
            Operation.Subtract => a - b,
            Operation.Negate => -a,
            Operation.Multiply => Round(a * b, Scale),
            Operation.Divide => Round(a * Scale, b),
            Operation.FromInt32 => unchecked((int)sample.Left) * (BigInteger)Scale,
            Operation.ToInt32 => a / Scale,
            _ => a < b ? BigInteger.One : BigInteger.Zero
        };
        if (raw < long.MinValue || raw > long.MaxValue)
            return (0, GaFaultKind.Overflow);
        return ((long)raw, 0);
    }

    private static BigInteger Round(BigInteger numerator, BigInteger divisor)
    {
        var quotient = BigInteger.DivRem(numerator, divisor, out var remainder);
        int compare = (BigInteger.Abs(remainder) * 2).CompareTo(BigInteger.Abs(divisor));
        return compare > 0 || compare == 0 && !quotient.IsEven
            ? quotient + numerator.Sign * divisor.Sign
            : quotient;
    }
}
