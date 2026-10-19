// StaticEcs uses MD5 only to derive deterministic component type GUIDs at startup.
// Its pinned sources remain unchanged. Native hosts retain the BCL implementation;
// browser hosts use the equivalent managed algorithm because MD5 is unavailable.
global using MD5 = SKECS.BenchmarkCompatibility.StaticEcsMd5;

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SKECS.BenchmarkCompatibility;

public sealed class StaticEcsMd5 : IDisposable
{
    private readonly System.Security.Cryptography.MD5 native;

    private StaticEcsMd5()
    {
        if (!OperatingSystem.IsBrowser()) native = System.Security.Cryptography.MD5.Create();
    }

    public static StaticEcsMd5 Create() => new();
    public byte[] ComputeHash(byte[] bytes) => native is null ? ComputeManaged(bytes) : native.ComputeHash(bytes);
    public void Dispose() => native?.Dispose();

    private static readonly int[] Shifts =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
    ];
    private static readonly uint[] Constants =
    [
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391
    ];

    // RFC 1321 block transform. Exposed for deterministic equivalence checks;
    // this compatibility hash is never used for security or simulation ticks.
    public static byte[] ComputeManaged(ReadOnlySpan<byte> input)
    {
        var paddedLength = checked((input.Length + 9 + 63) / 64 * 64);
        var padded = new byte[paddedLength];
        input.CopyTo(padded);
        padded[input.Length] = 0x80;
        BinaryPrimitives.WriteUInt64LittleEndian(padded.AsSpan(paddedLength - 8), (ulong)input.Length * 8);
        uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
        Span<uint> words = stackalloc uint[16];
        for (var offset = 0; offset < paddedLength; offset += 64)
        {
            for (var word = 0; word < 16; word++) words[word] = BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(offset + word * 4, 4));
            var a = a0; var b = b0; var c = c0; var d = d0;
            for (var round = 0; round < 64; round++)
            {
                uint f; int g;
                if (round < 16) { f = (b & c) | (~b & d); g = round; }
                else if (round < 32) { f = (d & b) | (~d & c); g = (5 * round + 1) % 16; }
                else if (round < 48) { f = b ^ c ^ d; g = (3 * round + 5) % 16; }
                else { f = c ^ (b | ~d); g = 7 * round % 16; }
                var next = unchecked(b + BitOperations.RotateLeft(unchecked(a + f + Constants[round] + words[g]), Shifts[round]));
                a = d; d = c; c = b; b = next;
            }
            a0 = unchecked(a0 + a); b0 = unchecked(b0 + b); c0 = unchecked(c0 + c); d0 = unchecked(d0 + d);
        }
        var result = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), a0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), b0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), c0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, 4), d0);
        return result;
    }
}
