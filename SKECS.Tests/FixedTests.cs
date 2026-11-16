using System.Numerics;
using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class FixedTests
{
    private const long Scale = 1L << 32;
    private const long Half = Scale / 2;

    [Fact]
    public unsafe void StorageAndConstantsFollowQ32_32()
    {
        Assert.Equal(8, sizeof(Fixed));
        Assert.Equal(0L, Fixed.Zero.Raw);
        Assert.Equal(Fixed.Zero, default);
        Assert.Equal(Scale, Fixed.One.Raw);
        Assert.Equal(long.MinValue, Fixed.MinValue.Raw);
        Assert.Equal(long.MaxValue, Fixed.MaxValue.Raw);
    }

    [Fact]
    public void ComparisonsFollowSignedRawValues()
    {
        var first = Fixed.FromRaw(-1);
        var second = Fixed.Zero;
        var copy = Fixed.FromRaw(-1);

        Assert.True(first < second);
        Assert.True(first <= copy);
        Assert.True(second > first);
        Assert.True(second >= first);
        Assert.True(first == copy);
        Assert.True(first != second);
        Assert.True(first.Equals(copy));
        Assert.True(first.Equals((object)copy));
        Assert.False(first.Equals((object)(-1L)));
        Assert.Equal(first.GetHashCode(), copy.GetHashCode());
        Assert.True(first.CompareTo(second) < 0);
        Assert.Equal(0, first.CompareTo(copy));
    }

    [Fact]
    public void ConversionsRespectRangeAndTruncateTowardZero()
    {
        Assert.Equal(Fixed.MinValue, Fixed.FromInt32(int.MinValue));
        Assert.Equal(Fixed.MinValue, (Fixed)(long)int.MinValue);
        Assert.Equal(((long)int.MaxValue) * Scale, Fixed.FromInt64(int.MaxValue).Raw);
        Assert.Equal(-Scale, ((Fixed)(-1)).Raw);
        Assert.Equal(Fixed.One, (Fixed)1L);
        Assert.Equal(int.MinValue, (int)Fixed.MinValue);
        Assert.Equal(int.MaxValue, (int)Fixed.MaxValue);
        Assert.Equal(1, (int)Fixed.FromRaw(Scale + Half));
        Assert.Equal(-1, (int)Fixed.FromRaw(-Scale - Half));
        Assert.Equal(0L, (long)Fixed.FromRaw(-1));
        Assert.Equal((long)int.MaxValue, (long)Fixed.MaxValue);
        Assert.Equal(-1.5f, (float)Fixed.FromRaw(-Scale - Half));
        Assert.Equal(1.0 / Scale, (double)Fixed.FromRaw(1));

        Assert.Throws<OverflowException>(() => Fixed.FromInt64((long)int.MinValue - 1));
        Assert.Throws<OverflowException>(() => unchecked((Fixed)((long)int.MaxValue + 1)));
    }

    [Fact]
    public void AdditionSubtractionAndNegationAreExactAndChecked()
    {
        Fixed quantum = Fixed.FromRaw(1);

        Assert.Equal(Fixed.MinValue, Fixed.MinValue + Fixed.Zero);
        Assert.Equal(Fixed.FromRaw(long.MaxValue - 1), Fixed.MaxValue - quantum);
        Assert.Equal(Fixed.FromRaw(-long.MaxValue), -Fixed.MaxValue);
        Assert.Equal(Fixed.One, checked(Fixed.Zero - (-Fixed.One)));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MaxValue + quantum));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MinValue + (-quantum)));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MinValue - quantum));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MaxValue - (-quantum)));
        Assert.Throws<OverflowException>(() => unchecked(-Fixed.MinValue));
    }

    [Theory]
    [InlineData(0L, long.MinValue, 0L)]
    [InlineData(Scale, long.MaxValue, long.MaxValue)]
    [InlineData(Scale, long.MinValue, long.MinValue)]
    [InlineData(-Scale, long.MaxValue, -long.MaxValue)]
    [InlineData(1L, Half - 1, 0L)]
    [InlineData(1L, Half, 0L)]
    [InlineData(1L, Half + 1, 1L)]
    [InlineData(3L, Half, 2L)]
    [InlineData(5L, Half, 2L)]
    [InlineData(-1L, Half, 0L)]
    [InlineData(1L, -Half - 1, -1L)]
    [InlineData(-3L, Half, -2L)]
    [InlineData(5L, -Half, -2L)]
    [InlineData(-3L, -Half, 2L)]
    [InlineData(Scale + 1, -(long.MaxValue - Half + 1), long.MinValue)]
    public void MultiplicationMatchesGoldenValues(long left, long right, long expected)
    {
        Assert.Equal(expected, (Fixed.FromRaw(left) * Fixed.FromRaw(right)).Raw);
        Assert.Equal(expected, (Fixed.FromRaw(right) * Fixed.FromRaw(left)).Raw);
    }

    [Theory]
    [InlineData(0L, long.MinValue, 0L)]
    [InlineData(long.MinValue, Scale, long.MinValue)]
    [InlineData(long.MaxValue, Scale, long.MaxValue)]
    [InlineData(long.MinValue, long.MinValue, Scale)]
    [InlineData(long.MaxValue, long.MinValue, -Scale)]
    [InlineData(long.MinValue, long.MaxValue, -Scale)]
    [InlineData(Half, -1L, long.MinValue)]
    [InlineData(1L, 3L, 1431655765L)]
    [InlineData(2L, 3L, 2863311531L)]
    [InlineData(1L, 2 * Scale + 1, 0L)]
    [InlineData(1L, 2 * Scale, 0L)]
    [InlineData(1L, 2 * Scale - 1, 1L)]
    [InlineData(3L, 2 * Scale, 2L)]
    [InlineData(5L, 2 * Scale, 2L)]
    [InlineData(-1L, 2 * Scale, 0L)]
    [InlineData(1L, -(2 * Scale - 1), -1L)]
    [InlineData(-3L, 2 * Scale, -2L)]
    [InlineData(5L, -2 * Scale, -2L)]
    [InlineData(-3L, -2 * Scale, 2L)]
    public void DivisionAndRatioMatchGoldenValues(long numerator, long denominator, long expected)
    {
        Assert.Equal(expected, (Fixed.FromRaw(numerator) / Fixed.FromRaw(denominator)).Raw);
        Assert.Equal(expected, Fixed.FromRatio(numerator, denominator).Raw);
    }

    [Fact]
    public void WideArithmeticReportsOverflowAndZeroDenominators()
    {
        // (2^32 + 1) * (2^63 - 2^31) / 2^32 equals long.MaxValue + 0.5.
        Fixed left = Fixed.FromRaw(Scale + 1);
        Fixed right = Fixed.FromRaw(long.MaxValue - Half + 1);

        Assert.Equal(long.MaxValue - 1, (left * Fixed.FromRaw(right.Raw - 1)).Raw);
        Assert.Throws<OverflowException>(() => unchecked(left * right));
        Assert.Throws<OverflowException>(() => unchecked(left * Fixed.FromRaw(-right.Raw - 1)));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MinValue * (-Fixed.One)));
        Assert.Throws<OverflowException>(() => unchecked(Fixed.MinValue / (-Fixed.One)));
        Assert.Throws<OverflowException>(() => Fixed.FromRatio(Half, 1));
        Assert.Throws<DivideByZeroException>(() => unchecked(Fixed.One / Fixed.Zero));
        Assert.Throws<DivideByZeroException>(() => unchecked(Fixed.Zero / Fixed.Zero));
        Assert.Throws<DivideByZeroException>(() => Fixed.FromRatio(long.MinValue, 0));
        Assert.Throws<DivideByZeroException>(() => Fixed.FromRatio(0, 0));
    }

    [Theory]
    [InlineData(0L, "0000000000000000")]
    [InlineData(Scale, "0000000001000000")]
    [InlineData(long.MinValue, "0000000000000080")]
    [InlineData(long.MaxValue, "FFFFFFFFFFFFFF7F")]
    [InlineData(-2L, "FEFFFFFFFFFFFFFF")]
    [InlineData(0x0123456789ABCDEFL, "EFCDAB8967452301")]
    public void SerializationMatchesGoldenBytesAndRoundTrips(long raw, string hex)
    {
        Assert.Equal(1, Fixed.NumericContractVersion);
        Assert.Equal(8, Fixed.SerializedSize);
        byte[] expected = Convert.FromHexString(hex);
        byte[] buffer = new byte[Fixed.SerializedSize + 2];
        Array.Fill(buffer, (byte)0xA5);
        Fixed.FromRaw(raw).WriteLittleEndian(buffer.AsSpan(1));

        Assert.Equal(expected, buffer[1..^1]);
        Assert.Equal(0xA5, buffer[0]);
        Assert.Equal(0xA5, buffer[^1]);
        Fixed restored = Fixed.ReadLittleEndian(buffer.AsSpan(1));
        Assert.Equal(raw, restored.Raw);

        var writtenAgain = new byte[Fixed.SerializedSize];
        restored.WriteLittleEndian(writtenAgain);
        Assert.Equal(expected, writtenAgain);
    }

    [Fact]
    public void SerializationRejectsShortBuffers()
    {
        var shortBuffer = new byte[Fixed.SerializedSize - 1];
        Array.Fill(shortBuffer, (byte)0xA5);

        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.One.WriteLittleEndian(shortBuffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.ReadLittleEndian(shortBuffer));
        Assert.All(shortBuffer, value => Assert.Equal(0xA5, value));
    }

    [Fact]
    public void WideArithmeticMatchesBigIntegerAcrossBoundariesAndRandomInputs()
    {
        long[] boundaries =
        [
            long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue,
            -2 * Scale, -Scale - 1, -Scale, -Scale + 1, -Half, -3, -2, -1, 0, 1, 2, 3,
            Half, Scale - 1, Scale, Scale + 1, 2 * Scale
        ];
        foreach (long a in boundaries)
        foreach (long b in boundaries)
            CheckWideArithmetic(a, b);

        ulong state = 0xBA78_EFA9_3DA6_481FUL;
        for (int i = 0; i < 2000; i++)
        {
            long a = NextRaw(ref state);
            long b = NextRaw(ref state);
            CheckWideArithmetic(a, b);
            CheckWideArithmetic(a >> 21, b >> 21); // Products with arbitrary fractional bits typically fit.
            CheckWideArithmetic(a, b >> 34); // Small denominators exercise the division overflow path.
        }
    }

    private static long NextRaw(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return unchecked((long)state);
    }

    private static void CheckWideArithmetic(long a, long b)
    {
        Fixed left = Fixed.FromRaw(a);
        Fixed right = Fixed.FromRaw(b);
        BigInteger product = RoundToEven((BigInteger)a * b, Scale);
        AssertRaw(product, () => (left * right).Raw, "multiplication", a, b);

        if (b == 0)
            return;

        BigInteger quotient = RoundToEven((BigInteger)a * Scale, b);
        AssertRaw(quotient, () => (left / right).Raw, "division", a, b);
        AssertRaw(quotient, () => Fixed.FromRatio(a, b).Raw, "ratio", a, b);
    }

    private static BigInteger RoundToEven(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        // Place the exact rational value between consecutive signed integers.
        BigInteger lower = BigInteger.DivRem(numerator, denominator, out BigInteger lowerDistance);
        if (lowerDistance.Sign < 0)
        {
            lower--;
            lowerDistance += denominator;
        }

        BigInteger upperDistance = denominator - lowerDistance;
        return lowerDistance < upperDistance || (lowerDistance == upperDistance && lower.IsEven)
            ? lower
            : lower + 1;
    }

    private static void AssertRaw(BigInteger expected, Func<long> operation, string name, long a, long b)
    {
        if (expected < long.MinValue || expected > long.MaxValue)
        {
            Assert.Throws<OverflowException>(() => operation());
            return;
        }

        long actual = operation();
        Assert.True(expected == actual, $"{name}({a}, {b}) produced raw {actual}, expected {expected}.");
    }
}
