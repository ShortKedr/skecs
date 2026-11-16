using System;

namespace SKECS;

/// <summary>Computes an entity's sort key from a copy of its declared sort component.</summary>
/// <remarks>Use a deterministic selector that depends only on the component value.</remarks>
public delegate DeterministicDomainKey DeterministicSourceKeySelector<T>(in T component) where T : unmanaged;

/// <summary>Defines a versioned source order for an entity kernel.</summary>
/// <remarks>Rows are sorted by key, then by entity ID and generation. The descriptor neither
/// filters query membership nor grants component access to the kernel processor.</remarks>
public sealed class DeterministicSourceOrder
{
    private readonly Func<World, Func<RunnerRow, DeterministicDomainKey>> _bind;

    private DeterministicSourceOrder(string contractId, int version, Func<World, Func<RunnerRow, DeterministicDomainKey>> bind)
    {
        ContractId = contractId;
        Version = version;
        _bind = bind;
    }

    public string ContractId { get; }
    public int Version { get; }

    public static DeterministicSourceOrder By<T>(string contractId, int version, DeterministicSourceKeySelector<T> select)
        where T : unmanaged
    {
        if (string.IsNullOrWhiteSpace(contractId))
            throw new ArgumentException("A source ordering contract needs an identifier.", nameof(contractId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (select is null) throw new ArgumentNullException(nameof(select));

        return new DeterministicSourceOrder(contractId, version, world =>
        {
            var typeId = world.GetComponentType<T>().Id;
            return row =>
            {
                if (!row.Archetype.HasComponent(typeId))
                    throw new InvalidOperationException(
                        $"Entity {row.Entity} selected by source order '{contractId}' is missing sort component {typeof(T).Name}.");
                var value = row.Read<T>(typeId);
                return select(in value);
            };
        });
    }

    internal Func<RunnerRow, DeterministicDomainKey> Bind(World world) => _bind(world);
}
