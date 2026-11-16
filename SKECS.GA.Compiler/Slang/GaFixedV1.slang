// SKECS.Numerics.Fixed.NumericContractVersion = 1.
// The low word comes first. No intermediate operation requires a wide device feature.
struct GaFixedResult
{
    uint2 bits;
    uint fault;
};

GaFixedResult gaFixedResult(uint2 bits, uint fault)
{
    GaFixedResult result;
    result.bits = bits;
    result.fault = fault;
    return result;
}

bool gaWideEqual(uint2 a, uint2 b)
{
    return a.x == b.x && a.y == b.y;
}

bool gaWideLess(uint2 a, uint2 b)
{
    return a.y < b.y || (a.y == b.y && a.x < b.x);
}

uint2 gaWideAdd(uint2 a, uint2 b)
{
    uint low = a.x + b.x;
    return uint2(low, a.y + b.y + uint(low < a.x));
}

uint2 gaWideSubtract(uint2 a, uint2 b)
{
    return uint2(a.x - b.x, a.y - b.y - uint(a.x < b.x));
}

uint2 gaWideNegate(uint2 a)
{
    return gaWideSubtract(uint2(0u, 0u), a);
}

bool gaWideNegative(uint2 a)
{
    return a.y >= 0x80000000u;
}

uint2 gaWideMagnitude(uint2 a)
{
    return gaWideNegative(a) ? gaWideNegate(a) : a;
}

bool gaFixedLess(uint2 a, uint2 b)
{
    a.y ^= 0x80000000u;
    b.y ^= 0x80000000u;
    return gaWideLess(a, b);
}

GaFixedResult gaFixedAdd(uint2 a, uint2 b)
{
    uint2 sum = gaWideAdd(a, b);
    bool overflow = gaWideNegative(a) == gaWideNegative(b) && gaWideNegative(a) != gaWideNegative(sum);
    return gaFixedResult(sum, uint(overflow));
}

GaFixedResult gaFixedSubtract(uint2 a, uint2 b)
{
    uint2 difference = gaWideSubtract(a, b);
    bool overflow = gaWideNegative(a) != gaWideNegative(b) && gaWideNegative(a) != gaWideNegative(difference);
    return gaFixedResult(difference, uint(overflow));
}

GaFixedResult gaFixedNegate(uint2 a)
{
    return gaFixedResult(gaWideNegate(a), uint(a.x == 0u && a.y == 0x80000000u));
}

uint2 gaFixedFromInt32(uint bits)
{
    return uint2(0u, bits);
}

uint gaFixedToInt32(uint2 a)
{
    return a.y + uint(gaWideNegative(a) && a.x != 0u);
}

uint2 gaMultiplyWord(uint a, uint b)
{
    uint aLow = a & 0xffffu;
    uint aHigh = a >> 16u;
    uint bLow = b & 0xffffu;
    uint bHigh = b >> 16u;
    uint low = aLow * bLow;
    uint cross = aHigh * bLow + (low >> 16u);
    uint middle = (cross & 0xffffu) + aLow * bHigh;
    uint high = aHigh * bHigh + (cross >> 16u) + (middle >> 16u);
    return uint2((middle << 16u) | (low & 0xffffu), high);
}

uint4 gaAdd128(uint4 a, uint4 b)
{
    uint carry = 0u;
    uint4 result;
    for (uint i = 0u; i < 4u; ++i)
    {
        uint low = a[i] + b[i];
        uint next = low + carry;
        carry = uint(low < a[i] || next < low);
        result[i] = next;
    }
    return result;
}

GaFixedResult gaFixedRounded(uint4 quotient, uint2 remainder, uint2 divisor, bool negative)
{
    uint2 distanceToNext = gaWideSubtract(divisor, remainder);
    if (gaWideLess(distanceToNext, remainder) ||
        (gaWideEqual(distanceToNext, remainder) && (quotient.x & 1u) != 0u))
        quotient = gaAdd128(quotient, uint4(1u, 0u, 0u, 0u));

    uint2 limit = negative ? uint2(0u, 0x80000000u) : uint2(0xffffffffu, 0x7fffffffu);
    if (quotient.z != 0u || quotient.w != 0u || gaWideLess(limit, quotient.xy))
        return gaFixedResult(uint2(0u, 0u), 1u);
    return gaFixedResult(negative ? gaWideNegate(quotient.xy) : quotient.xy, 0u);
}

GaFixedResult gaFixedMultiply(uint2 signedA, uint2 signedB)
{
    uint2 a = gaWideMagnitude(signedA);
    uint2 b = gaWideMagnitude(signedB);
    uint2 low = gaMultiplyWord(a.x, b.x);
    uint2 crossLeft = gaMultiplyWord(a.y, b.x);
    uint2 crossRight = gaMultiplyWord(a.x, b.y);
    uint2 high = gaMultiplyWord(a.y, b.y);
    uint4 product = gaAdd128(uint4(low, high), uint4(0u, crossLeft, 0u));
    product = gaAdd128(product, uint4(0u, crossRight, 0u));
    return gaFixedRounded(uint4(product.yzw, 0u), uint2(product.x, 0u), uint2(0u, 1u),
        gaWideNegative(signedA) != gaWideNegative(signedB));
}

// FromRatio and division consume the same signed raw words and apply the same scaling.
GaFixedResult gaFixedDivide(uint2 signedA, uint2 signedB)
{
    uint2 divisor = gaWideMagnitude(signedB);
    if (gaWideEqual(divisor, uint2(0u, 0u)))
        return gaFixedResult(uint2(0u, 0u), 2u);

    uint2 a = gaWideMagnitude(signedA);
    uint3 numerator = uint3(0u, a);
    uint2 remainder = uint2(0u, 0u);
    uint4 quotient = uint4(0u, 0u, 0u, 0u);
    for (int bit = 95; bit >= 0; --bit)
    {
        uint word = uint(bit) >> 5u;
        uint offset = uint(bit) & 31u;
        uint next = (numerator[word] >> offset) & 1u;
        // remainder < divisor <= 2^63, so appending one bit fits in two words.
        remainder = uint2((remainder.x << 1u) | next, (remainder.y << 1u) | (remainder.x >> 31u));
        if (!gaWideLess(remainder, divisor))
        {
            remainder = gaWideSubtract(remainder, divisor);
            quotient[word] |= 1u << offset;
        }
    }
    return gaFixedRounded(quotient, remainder, divisor, gaWideNegative(signedA) != gaWideNegative(signedB));
}
