using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKECS.Determinism;

internal sealed record Operation(string Kind, int Key = 0, int A = 0, int B = 0);
internal sealed record Fixture(int FormatVersion, int Capacity, Operation[] Operations)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static Fixture Read(byte[] bytes)
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(bytes, Json);
        if (fixture is not { FormatVersion: 1, Operations: not null } ||
            fixture.Operations.Any(operation => string.IsNullOrEmpty(operation?.Kind)))
            throw new InvalidDataException("Expected a version 1 fixture with a complete operation list.");
        return fixture;
    }
}

internal sealed record ReplayOptions(
    int Capacity, bool Diagnostics = false, bool WarmQueries = true,
    bool ReverseRegistration = false, bool UseChunks = false);

internal readonly record struct Transform(int X, int Y);
internal readonly record struct Motion(int Dx, int Dy);
internal readonly record struct Marker(byte Kind, int Value);

internal sealed record EntityState(
    int Key, int Id, int Version, Transform? Transform, Motion? Motion, Marker? Marker);
internal sealed record ReplayStep(
    int Step, string Operation, string[] Result, string[] Visits, EntityState[] Entities, string Hash);
