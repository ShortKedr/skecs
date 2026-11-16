using System;
using System.Buffers.Binary;
using System.IO;

namespace SKECS.Network;

internal static class CheckpointDelta
{
    private const int HeaderBytes = 2 * sizeof(int);
    private const int RunHeaderBytes = 2 * sizeof(int);

    internal static byte[]? Encode(ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> target)
    {
        if (target.Length <= HeaderBytes)
            return null;

        int suffixLength = 0;
        if (baseline.Length != target.Length)
        {
            int maxSuffixLength = Math.Min(baseline.Length, target.Length);
            while (suffixLength < maxSuffixLength &&
                   baseline[^(suffixLength + 1)] == target[^(suffixLength + 1)])
            {
                suffixLength++;
            }
        }

        baseline = baseline[..(baseline.Length - suffixLength)];
        target = target[..(target.Length - suffixLength)];
        int targetLength = target.Length + suffixLength;
        int encodedLength = HeaderBytes;
        int position = 0;
        while (FindRun(baseline, target, ref position, out int start))
        {
            int length = position - start;
            if (targetLength - encodedLength - RunHeaderBytes <= length)
                return null;

            encodedLength += RunHeaderBytes + length;
        }

        byte[] delta = new byte[encodedLength];
        BinaryPrimitives.WriteInt32LittleEndian(delta, targetLength);
        BinaryPrimitives.WriteInt32LittleEndian(delta.AsSpan(sizeof(int)), suffixLength);

        int output = HeaderBytes;
        position = 0;
        while (FindRun(baseline, target, ref position, out int start))
        {
            int length = position - start;
            BinaryPrimitives.WriteInt32LittleEndian(delta.AsSpan(output), start);
            BinaryPrimitives.WriteInt32LittleEndian(delta.AsSpan(output + sizeof(int)), length);
            output += RunHeaderBytes;
            target.Slice(start, length).CopyTo(delta.AsSpan(output));
            output += length;
        }

        return delta;
    }

    internal static byte[] Decode(ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> delta, int maxTargetBytes)
    {
        if (delta.Length < HeaderBytes)
            throw new EndOfStreamException("Truncated checkpoint delta header.");

        int targetLength = BinaryPrimitives.ReadInt32LittleEndian(delta);
        int suffixLength = BinaryPrimitives.ReadInt32LittleEndian(delta[sizeof(int)..]);
        if (targetLength <= delta.Length || targetLength > maxTargetBytes ||
            suffixLength < 0 || suffixLength > Math.Min(baseline.Length, targetLength) ||
            (baseline.Length == targetLength && suffixLength != 0))
        {
            throw new InvalidDataException("Invalid checkpoint delta header.");
        }

        int baselinePrefixLength = baseline.Length - suffixLength;
        int targetPrefixLength = targetLength - suffixLength;
        ReadOnlySpan<byte> baselinePrefix = baseline[..baselinePrefixLength];
        ReadOnlySpan<byte> runs = delta[HeaderBytes..];
        int previousEnd = 0;
        bool hasRun = false;
        int sharedPrefixLength = Math.Min(baselinePrefixLength, targetPrefixLength);
        byte lastPrefixByte = sharedPrefixLength == targetPrefixLength && targetPrefixLength > 0
            ? baselinePrefix[targetPrefixLength - 1]
            : (byte)0;

        // Validate every run before allocating the output.
        while (!runs.IsEmpty)
        {
            ReadRun(ref runs, out int start, out ReadOnlySpan<byte> bytes);
            if (start < previousEnd || (hasRun && start == previousEnd) ||
                start > Math.Max(previousEnd, sharedPrefixLength) ||
                bytes.Length > targetPrefixLength - start)
            {
                throw new InvalidDataException("Invalid checkpoint delta run.");
            }

            int comparedLength = Math.Min(bytes.Length, Math.Max(0, sharedPrefixLength - start));
            for (int i = 0; i < comparedLength; i++)
            {
                if (bytes[i] == baselinePrefix[start + i])
                    throw new InvalidDataException("Checkpoint delta run contains an unchanged byte.");
            }

            previousEnd = start + bytes.Length;
            if (previousEnd == targetPrefixLength)
                lastPrefixByte = bytes[^1];

            hasRun = true;
        }

        if (Math.Max(previousEnd, sharedPrefixLength) < targetPrefixLength)
            throw new InvalidDataException("Checkpoint delta leaves target bytes undefined.");

        if (baseline.Length != targetLength && baselinePrefixLength > 0 && targetPrefixLength > 0 &&
            lastPrefixByte == baselinePrefix[^1])
        {
            throw new InvalidDataException("Checkpoint delta suffix is shorter than the matching suffix.");
        }

        byte[] result = new byte[targetLength];
        baselinePrefix[..sharedPrefixLength].CopyTo(result);
        baseline[baselinePrefixLength..].CopyTo(result.AsSpan(targetPrefixLength));
        runs = delta[HeaderBytes..];
        while (!runs.IsEmpty)
        {
            ReadRun(ref runs, out int start, out ReadOnlySpan<byte> bytes);
            bytes.CopyTo(result.AsSpan(start));
        }

        return result;
    }

    private static bool FindRun(ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> target,
        ref int position, out int start)
    {
        while (position < target.Length && position < baseline.Length &&
               baseline[position] == target[position])
        {
            position++;
        }

        start = position;
        while (position < target.Length &&
               (position >= baseline.Length || baseline[position] != target[position]))
        {
            position++;
        }

        return start != position;
    }

    private static void ReadRun(ref ReadOnlySpan<byte> runs, out int start, out ReadOnlySpan<byte> bytes)
    {
        if (runs.Length < RunHeaderBytes)
            throw new EndOfStreamException("Truncated checkpoint delta run header.");

        start = BinaryPrimitives.ReadInt32LittleEndian(runs);
        int length = BinaryPrimitives.ReadInt32LittleEndian(runs[sizeof(int)..]);
        if (start < 0 || length <= 0)
            throw new InvalidDataException("Invalid checkpoint delta run header.");

        runs = runs[RunHeaderBytes..];
        if (length > runs.Length)
            throw new EndOfStreamException("Truncated checkpoint delta run payload.");

        bytes = runs[..length];
        runs = runs[length..];
    }
}
