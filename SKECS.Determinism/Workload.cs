using System.Text.Json;

namespace SKECS.Determinism;

internal static class Workload
{
    internal static IEnumerable<ReplayStep> Replay(Fixture fixture, ReplayOptions? options = null)
    {
        if (fixture.FormatVersion != 1)
            throw new InvalidDataException($"Unknown fixture version {fixture.FormatVersion}.");

        using var native = new NativeWorld(options ?? new ReplayOptions(fixture.Capacity));
        var oracle = new ManagedWorld();
        for (var step = 0; step <= fixture.Operations.Length; step++)
        {
            var input = step == 0 ? new Operation("initial") : fixture.Operations[step - 1];
            ReplayStep output;
            try
            {
                if (step != 0 && input.Kind == "initial")
                    throw new InvalidDataException("'initial' is reserved for the starting state.");
                oracle.Apply(input);
                native.Apply(input);

                var expected = oracle.Capture();
                var actual = native.Capture();
                RequireEqual(Snapshot.StateDifference(expected, actual));
                RequireEqual(Snapshot.SequenceDifference("result", oracle.Results, native.Results));
                RequireEqual(Snapshot.SequenceDifference("query membership",
                    oracle.Visits.Order(StringComparer.Ordinal).ToArray(),
                    native.Visits.Order(StringComparer.Ordinal).ToArray()));
                RequireEqual(Snapshot.SequenceDifference("systems",
                    oracle.Systems.Select(frame => frame.Name).ToArray(),
                    native.Systems.Select(frame => frame.Name).ToArray()));
                for (var system = 0; system < oracle.Systems.Count; system++)
                {
                    var frame = oracle.Systems[system];
                    var nativeFrame = native.Systems[system];
                    var difference = Snapshot.StateDifference(frame.Entities, nativeFrame.Entities);
                    if (difference is not null)
                        throw new InvalidDataException($"System {frame.Name}, {difference}\n" +
                            $"Expected system state: {Describe(() => frame.Entities)}\n" +
                            $"Actual system state: {Describe(() => nativeFrame.Entities)}");
                }

                output = new ReplayStep(step, input.Kind,
                    native.Results.Concat(native.Systems.Select(frame =>
                        $"system:{frame.Name}:{Snapshot.Hash(frame.Entities)}")).ToArray(),
                    native.Visits.ToArray(), actual, Snapshot.Hash(actual));
            }
            catch (Exception error)
            {
                throw new InvalidDataException(
                    $"Step {step}, {input}: {error.Message}\n" +
                    $"Expected: {Describe(oracle.Capture)}\n" +
                    $"Actual: {Describe(native.Capture)}", error);
            }
            yield return output;
        }
    }

    private static void RequireEqual(string? difference)
    {
        if (difference is not null)
            throw new InvalidDataException(difference);
    }

    private static string Describe(Func<EntityState[]> capture)
    {
        try
        {
            return JsonSerializer.Serialize(capture(), Fixture.Json);
        }
        catch (Exception error)
        {
            return $"could not capture state ({error.Message})";
        }
    }
}

internal sealed record SystemFrame(string Name, EntityState[] Entities);

internal static class Trace
{
    internal static string Identity(int id, int version)
        => FormattableString.Invariant($"{id}:{version}");

    internal static string Created(int key, int id, int version)
        => FormattableString.Invariant($"created:{key}={Identity(id, version)}");

    internal static string Probe(int key, bool alive, Transform? value)
        => FormattableString.Invariant($"probe:{key}:alive={alive}:transform={value?.X},{value?.Y}");

    internal static string Visit(string query, int id, int version)
        => $"{query}={Identity(id, version)}";
}
