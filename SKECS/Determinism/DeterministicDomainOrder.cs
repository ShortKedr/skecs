using System;

namespace SKECS;

/// <summary>Identifies the domain ordering contract selected for a commit group.</summary>
public sealed class DeterministicDomainOrder
{
    public DeterministicDomainOrder(string contractId, int version)
    {
        if (string.IsNullOrWhiteSpace(contractId))
            throw new ArgumentException("A domain ordering contract needs an identifier.", nameof(contractId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ContractId = contractId;
        Version = version;
    }

    public string ContractId { get; }
    public int Version { get; }
}

/// <summary>Two signed coordinates compared lexicographically at a domain-ordered barrier.</summary>
public readonly struct DeterministicDomainKey : IComparable<DeterministicDomainKey>, IEquatable<DeterministicDomainKey>
{
    public DeterministicDomainKey(int primary, int secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public int Primary { get; }
    public int Secondary { get; }

    public int CompareTo(DeterministicDomainKey other)
    {
        int order = Primary.CompareTo(other.Primary);
        return order != 0 ? order : Secondary.CompareTo(other.Secondary);
    }

    public bool Equals(DeterministicDomainKey other) => Primary == other.Primary && Secondary == other.Secondary;

    public override bool Equals(object? obj) => obj is DeterministicDomainKey other && Equals(other);

    public override int GetHashCode() => unchecked(Primary * 31 + Secondary);
}
