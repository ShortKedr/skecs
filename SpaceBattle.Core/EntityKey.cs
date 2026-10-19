namespace SpaceBattle;

public readonly record struct EntityKey(int Id, int Generation)
{
    public static readonly EntityKey None = new(-1, 0);
    public bool IsNone => Id < 0;
}

public readonly record struct StoreDiagnostics(int Entities, int? Archetypes, int? Chunks, long? NativeBytes, long? AccessorBytes);
