using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using SKECS.Numerics;

namespace SKECS.Benchmarks.Numerics;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class FixedBenchmarks
{
    private const int Count = 1024;
    private const long FixedScale = 1L << 32;
    private const long IntegerScale = 1L << 16;
    private const float FloatStep = 1f / 16;
    private const long IntegerStep = IntegerScale / 16;
    private static readonly Fixed FixedStep = Fixed.FromRatio(1, 16);

    private readonly Buffers<Fixed> _fixed = new();
    private readonly Buffers<float> _float = new();
    private readonly Buffers<long> _integer = new();

    [GlobalSetup]
    public void Validate()
    {
        for (int i = 0; i < Count; i++)
        {
            int left = (i * 241 % 2048) - 1024;
            int right = (2 + i % 17) * (i % 2 == 0 ? 1 : -1);
            int acceleration = (i * 53 % 32) - 16;
            _fixed.Left[i] = Fixed.FromRatio(left, 8);
            _fixed.Right[i] = Fixed.FromRatio(right, 2);
            _fixed.Acceleration[i] = Fixed.FromRatio(acceleration, 8);
            _float.Left[i] = left / 8f;
            _float.Right[i] = right / 2f;
            _float.Acceleration[i] = acceleration / 8f;
            _integer.Left[i] = left * (IntegerScale / 8);
            _integer.Right[i] = right * (IntegerScale / 2);
            _integer.Acceleration[i] = acceleration * (IntegerScale / 8);
        }

        FixedAdd(); FloatAdd(); IntegerAdd();
        CheckResults((left, right) => left + right);
        FixedMultiply(); FloatMultiply(); IntegerMultiply();
        CheckResults((left, right) => left * right);
        FixedDivide(); FloatDivide(); IntegerDivide();
        CheckResults((left, right) => left / right);
        FixedIntegrate(); FloatIntegrate(); IntegerIntegrate();
        for (int i = 0; i < Count; i++)
        {
            decimal velocity = (decimal)_float.Right[i] + (decimal)_float.Acceleration[i] / 16;
            decimal position = (decimal)_float.Left[i] + velocity / 16;
            CheckValue(velocity, _fixed.Velocity[i], _float.Velocity[i], _integer.Velocity[i], i);
            CheckValue(position, _fixed.Result[i], _float.Result[i], _integer.Result[i], i);
        }
    }

    [BenchmarkCategory("Add")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void FixedAdd()
    {
        for (int i = 0; i < Count; i++)
            _fixed.Result[i] = _fixed.Left[i] + _fixed.Right[i];
    }

    [BenchmarkCategory("Add")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public void FloatAdd()
    {
        for (int i = 0; i < Count; i++)
            _float.Result[i] = _float.Left[i] + _float.Right[i];
    }

    [BenchmarkCategory("Add")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void IntegerAdd()
    {
        for (int i = 0; i < Count; i++)
            _integer.Result[i] = _integer.Left[i] + _integer.Right[i];
    }

    [BenchmarkCategory("Multiply")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void FixedMultiply()
    {
        for (int i = 0; i < Count; i++)
            _fixed.Result[i] = _fixed.Left[i] * _fixed.Right[i];
    }

    [BenchmarkCategory("Multiply")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public void FloatMultiply()
    {
        for (int i = 0; i < Count; i++)
            _float.Result[i] = _float.Left[i] * _float.Right[i];
    }

    [BenchmarkCategory("Multiply")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void IntegerMultiply()
    {
        for (int i = 0; i < Count; i++)
            _integer.Result[i] = _integer.Left[i] * _integer.Right[i] / IntegerScale;
    }

    [BenchmarkCategory("Divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void FixedDivide()
    {
        for (int i = 0; i < Count; i++)
            _fixed.Result[i] = _fixed.Left[i] / _fixed.Right[i];
    }

    [BenchmarkCategory("Divide")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public void FloatDivide()
    {
        for (int i = 0; i < Count; i++)
            _float.Result[i] = _float.Left[i] / _float.Right[i];
    }

    [BenchmarkCategory("Divide")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void IntegerDivide()
    {
        for (int i = 0; i < Count; i++)
        {
            long dividend = _integer.Left[i] * IntegerScale;
            long divisor = _integer.Right[i];
            long quotient = Math.DivRem(dividend, divisor, out long remainder);
            long halfComparison = 2 * Math.Abs(remainder) - Math.Abs(divisor);
            if (halfComparison > 0 || (halfComparison == 0 && (quotient & 1) != 0))
                quotient += Math.Sign(dividend) * Math.Sign(divisor);
            _integer.Result[i] = quotient;
        }
    }

    [BenchmarkCategory("Integrate")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void FixedIntegrate()
    {
        for (int i = 0; i < Count; i++)
        {
            Fixed velocity = _fixed.Right[i] + _fixed.Acceleration[i] * FixedStep;
            _fixed.Velocity[i] = velocity;
            _fixed.Result[i] = _fixed.Left[i] + velocity * FixedStep;
        }
    }

    [BenchmarkCategory("Integrate")]
    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public void FloatIntegrate()
    {
        for (int i = 0; i < Count; i++)
        {
            float velocity = _float.Right[i] + _float.Acceleration[i] * FloatStep;
            _float.Velocity[i] = velocity;
            _float.Result[i] = _float.Left[i] + velocity * FloatStep;
        }
    }

    [BenchmarkCategory("Integrate")]
    [Benchmark(OperationsPerInvoke = Count)]
    public void IntegerIntegrate()
    {
        for (int i = 0; i < Count; i++)
        {
            long velocity = _integer.Right[i] + _integer.Acceleration[i] * IntegerStep / IntegerScale;
            _integer.Velocity[i] = velocity;
            _integer.Result[i] = _integer.Left[i] + velocity * IntegerStep / IntegerScale;
        }
    }

    private void CheckResults(Func<decimal, decimal, decimal> calculate)
    {
        for (int i = 0; i < Count; i++)
        {
            decimal expected = calculate((decimal)_float.Left[i], (decimal)_float.Right[i]);
            CheckValue(expected, _fixed.Result[i], _float.Result[i], _integer.Result[i], i);
        }
    }

    private static void CheckValue(decimal expected, Fixed fixedValue, float floatValue, long integerValue, int index)
    {
        if (fixedValue.Raw != (long)decimal.Round(expected * FixedScale) ||
            integerValue != (long)decimal.Round(expected * IntegerScale) ||
            MathF.Abs(floatValue - (float)expected) > 0.00001f)
            throw new InvalidOperationException($"Invalid numeric benchmark result at index {index} for expected value {expected}.");
    }

    private sealed class Buffers<T>
    {
        public readonly T[] Left = new T[Count];
        public readonly T[] Right = new T[Count];
        public readonly T[] Acceleration = new T[Count];
        public readonly T[] Result = new T[Count];
        public readonly T[] Velocity = new T[Count];
    }
}
