using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SKECS.Numerics;

/// <summary>A signed Q32.32 number with deterministic, checked arithmetic.</summary>
/// <remarks>
/// Represents values from -2^31 through 2^31 - 2^-32 in steps of 2^-32.
/// Multiplication, division and ratio creation round to the nearest raw value, with ties to even.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    private const int FractionalBits = 32;
    private const long Scale = 1L << FractionalBits;

    /// <summary>Version of the numeric rules and little-endian raw encoding.</summary>
    public const int NumericContractVersion = 1;

    /// <summary>Number of bytes in the raw encoding.</summary>
    public const int SerializedSize = sizeof(long);

    public static readonly Fixed Zero = default;
    public static readonly Fixed One = new Fixed(Scale);
    public static readonly Fixed MinValue = new Fixed(long.MinValue);
    public static readonly Fixed MaxValue = new Fixed(long.MaxValue);

    /// <summary>Signed representation in units of 2^-32.</summary>
    public readonly long Raw;

    private Fixed(long raw) => Raw = raw;

    public static Fixed FromRaw(long raw) => new Fixed(raw);

    public static Fixed FromInt32(int value) => new Fixed((long)value << FractionalBits);

    /// <exception cref="OverflowException">The value is outside the signed 32-bit integer range.</exception>
    public static Fixed FromInt64(long value) => FromInt32(checked((int)value));

    /// <summary>Creates the nearest representable value to the given integer ratio, with ties to even.</summary>
    /// <exception cref="DivideByZeroException">The denominator is zero.</exception>
    /// <exception cref="OverflowException">The rounded result is outside the Q32.32 range.</exception>
    public static Fixed FromRatio(long numerator, long denominator) => Divide(numerator, denominator);

    /// <summary>Writes the raw signed integer to the first eight bytes, little-endian.</summary>
    /// <remarks>Store <see cref="NumericContractVersion"/> in the containing format.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The destination has fewer than eight bytes.</exception>
    public void WriteLittleEndian(Span<byte> destination) => BinaryPrimitives.WriteInt64LittleEndian(destination, Raw);

    /// <summary>Reads the raw signed integer from the first eight bytes, little-endian.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The source has fewer than eight bytes.</exception>
    public static Fixed ReadLittleEndian(ReadOnlySpan<byte> source) => FromRaw(BinaryPrimitives.ReadInt64LittleEndian(source));

    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(Fixed left, Fixed right) => left.Raw == right.Raw;
    public static bool operator !=(Fixed left, Fixed right) => left.Raw != right.Raw;
    public static bool operator <(Fixed left, Fixed right) => left.Raw < right.Raw;
    public static bool operator <=(Fixed left, Fixed right) => left.Raw <= right.Raw;
    public static bool operator >(Fixed left, Fixed right) => left.Raw > right.Raw;
    public static bool operator >=(Fixed left, Fixed right) => left.Raw >= right.Raw;

    public static explicit operator Fixed(int value) => FromInt32(value);
    public static explicit operator Fixed(long value) => FromInt64(value);

    /// <summary>Converts to an integer by truncating toward zero.</summary>
    public static explicit operator int(Fixed value) => (int)(value.Raw / Scale);

    /// <summary>Converts to an integer by truncating toward zero.</summary>
    public static explicit operator long(Fixed value) => value.Raw / Scale;

    public static explicit operator float(Fixed value) => (float)value.Raw / Scale;
    public static explicit operator double(Fixed value) => (double)value.Raw / Scale;

    public static Fixed operator +(Fixed left, Fixed right) => FromRaw(checked(left.Raw + right.Raw));
    public static Fixed operator -(Fixed left, Fixed right) => FromRaw(checked(left.Raw - right.Raw));
    public static Fixed operator -(Fixed value) => FromRaw(checked(-value.Raw));

    public static Fixed operator *(Fixed left, Fixed right)
    {
        ulong leftMagnitude = Magnitude(left.Raw);
        ulong rightMagnitude = Magnitude(right.Raw);
        ulong leftLow = unchecked((uint)leftMagnitude);
        ulong rightLow = unchecked((uint)rightMagnitude);
        ulong leftHigh = leftMagnitude >> FractionalBits;
        ulong rightHigh = rightMagnitude >> FractionalBits;

        // Form the wide product in 32-bit limbs. The low limb is the remainder modulo Scale.
        ulong low = leftLow * rightLow;
        ulong crossLeft = leftHigh * rightLow;
        ulong crossRight = leftLow * rightHigh;
        ulong middle = (low >> FractionalBits) + unchecked((uint)crossLeft) + unchecked((uint)crossRight);
        ulong high = leftHigh * rightHigh + (crossLeft >> FractionalBits) + (crossRight >> FractionalBits) + (middle >> FractionalBits);
        if (high > uint.MaxValue)
            throw new OverflowException();

        ulong quotient = (high << FractionalBits) | unchecked((uint)middle);
        ulong remainder = unchecked((uint)low);
        return Round(quotient, remainder, Scale, (left.Raw < 0) != (right.Raw < 0));
    }

    public static Fixed operator /(Fixed left, Fixed right) => Divide(left.Raw, right.Raw);

    private static Fixed Divide(long numerator, long denominator)
    {
        // Some AOT runtimes lower integer division to a native instruction without a managed zero check.
        if (denominator == 0)
            throw new DivideByZeroException();

        ulong divisor = Magnitude(denominator);
        ulong dividend = Magnitude(numerator);
        ulong whole = dividend / divisor;
        if (whole > uint.MaxValue)
            throw new OverflowException();

        ulong remainder = dividend - whole * divisor;
        ulong fraction = 0;
        if (remainder <= uint.MaxValue)
        {
            ulong scaled = remainder << FractionalBits;
            fraction = scaled / divisor;
            remainder = scaled - fraction * divisor;
        }
        else
        {
            // Emit the fractional quotient by binary long division. The divisor is at most 2^63,
            // so doubling a remainder strictly smaller than the divisor fits in 64 unsigned bits.
            for (int bit = 0; bit < FractionalBits; bit++)
            {
                remainder <<= 1;
                fraction <<= 1;
                if (remainder >= divisor)
                {
                    remainder -= divisor;
                    fraction |= 1;
                }
            }
        }

        return Round((whole << FractionalBits) | fraction, remainder, divisor, (numerator < 0) != (denominator < 0));
    }

    private static ulong Magnitude(long raw) => raw < 0 ? unchecked(0UL - (ulong)raw) : (ulong)raw;

    private static Fixed Round(ulong quotient, ulong remainder, ulong divisor, bool negative)
    {
        ulong distanceToNext = divisor - remainder;
        if (remainder > distanceToNext || (remainder == distanceToNext && (quotient & 1) != 0))
            quotient = checked(quotient + 1);

        if (negative)
        {
            if (quotient > (1UL << 63))
                throw new OverflowException();
            return FromRaw(unchecked(-(long)quotient));
        }

        return FromRaw(checked((long)quotient));
    }
}
