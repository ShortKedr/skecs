using System.Security.Cryptography;

namespace SKECS.Determinism;

internal static class Snapshot
{
    internal static string Hash(IEnumerable<EntityState> entities, bool logicalOnly = false)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        var ordered = Order(entities, logicalOnly);
        writer.Write(1); // Replay state projection version.
        writer.Write(logicalOnly);
        writer.Write(ordered.Length);
        foreach (var entity in ordered)
        {
            writer.Write(entity.Key);
            if (!logicalOnly)
            {
                writer.Write(entity.Id);
                writer.Write(entity.Version);
            }

            writer.Write((entity.Transform.HasValue ? 1 : 0) + (entity.Motion.HasValue ? 1 : 0) +
                (entity.Marker.HasValue ? 1 : 0));
            if (entity.Transform is { } transform)
            {
                writer.Write(ReplaySchema.TransformId);
                writer.Write(ReplaySchema.First);
                writer.Write(transform.X);
                writer.Write(ReplaySchema.Second);
                writer.Write(transform.Y);
            }
            if (entity.Motion is { } motion)
            {
                writer.Write(ReplaySchema.MotionId);
                writer.Write(ReplaySchema.First);
                writer.Write(motion.Dx);
                writer.Write(ReplaySchema.Second);
                writer.Write(motion.Dy);
            }
            if (entity.Marker is { } marker)
            {
                writer.Write(ReplaySchema.MarkerId);
                writer.Write(ReplaySchema.First);
                writer.Write(marker.Kind);
                writer.Write(ReplaySchema.Second);
                writer.Write(marker.Value);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length))).ToLowerInvariant();
    }

    internal static EntityState[] Order(IEnumerable<EntityState> entities, bool logicalOnly = false)
        => logicalOnly
            ? entities.OrderBy(entity => entity.Key).ToArray()
            : entities.OrderBy(entity => entity.Id).ThenBy(entity => entity.Version).ToArray();

    internal static string? Difference(ReplayStep expected, ReplayStep actual, bool logicalOnly = false)
    {
        if (expected.Step != actual.Step)
            return Value("step", expected.Step, actual.Step);
        if (expected.Operation != actual.Operation)
            return Value("operation", expected.Operation, actual.Operation);

        var state = StateDifference(expected.Entities, actual.Entities, logicalOnly);
        if (state is not null || logicalOnly)
            return state;

        return SequenceDifference("result", expected.Result, actual.Result)
            ?? SequenceDifference("visits", expected.Visits, actual.Visits)
            ?? (expected.Hash != actual.Hash ? Value("hash", expected.Hash, actual.Hash) : null);
    }

    internal static string? StateDifference(
        IEnumerable<EntityState> expected, IEnumerable<EntityState> actual, bool logicalOnly = false)
    {
        var left = expected.ToDictionary(entity => entity.Key);
        var right = actual.ToDictionary(entity => entity.Key);
        foreach (var key in left.Keys.Union(right.Keys).Order())
        {
            var path = $"entity {key}";
            if (!left.TryGetValue(key, out var a))
                return $"{path} is unexpected";
            if (!right.TryGetValue(key, out var b))
                return $"{path} is missing";
            if (!logicalOnly && (a.Id != b.Id || a.Version != b.Version))
                return Value($"{path} id:version", $"{a.Id}:{a.Version}", $"{b.Id}:{b.Version}");

            var difference = ComponentDifference($"{path} transform", a.Transform, b.Transform,
                    (x, y) => x.X != y.X ? Value("x", x.X, y.X) : Value("y", x.Y, y.Y))
                ?? ComponentDifference($"{path} motion", a.Motion, b.Motion,
                    (x, y) => x.Dx != y.Dx ? Value("dx", x.Dx, y.Dx) : Value("dy", x.Dy, y.Dy))
                ?? ComponentDifference($"{path} marker", a.Marker, b.Marker,
                    (x, y) => x.Kind != y.Kind ? Value("kind", x.Kind, y.Kind) : Value("value", x.Value, y.Value));
            if (difference is not null)
                return difference;
        }
        return null;
    }

    private static string? ComponentDifference<T>(string path, T? expected, T? actual, Func<T, T, string> fields)
        where T : struct
    {
        if (Nullable.Equals(expected, actual))
            return null;
        if (expected is { } a && actual is { } b)
            return $"{path} {fields(a, b)}";
        return Value($"{path} membership", expected.HasValue, actual.HasValue);
    }

    internal static string? SequenceDifference(string path, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            if (expected[i] != actual[i])
                return Value($"{path}[{i}]", expected[i], actual[i]);
        }
        return expected.Count != actual.Count ? Value($"{path} count", expected.Count, actual.Count) : null;
    }

    private static string Value<T>(string path, T expected, T actual)
        => FormattableString.Invariant($"{path}: expected {expected}, actual {actual}");
}
