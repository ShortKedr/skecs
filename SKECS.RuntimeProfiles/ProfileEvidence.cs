namespace SKECS.RuntimeProfiles;

/// <summary>Identities of the corpus and paired verification report used to qualify the profile.</summary>
public sealed record ProfileEvidence(string VerifierVersion, string CorpusSha256, string VerificationSha256,
    int TrainingScenarios, int HoldoutScenarios, int Repetitions)
{
    internal void Validate()
    {
        ProfileIdentity.RequireText(VerifierVersion, nameof(VerifierVersion));
        ProfileIdentity.RequireSha256(CorpusSha256, nameof(CorpusSha256));
        ProfileIdentity.RequireSha256(VerificationSha256, nameof(VerificationSha256));
        if (TrainingScenarios <= 0) throw new ArgumentOutOfRangeException(nameof(TrainingScenarios));
        if (HoldoutScenarios <= 0) throw new ArgumentOutOfRangeException(nameof(HoldoutScenarios));
        if (Repetitions < 2) throw new ArgumentOutOfRangeException(nameof(Repetitions));
    }
}

/// <summary>Content hashes for each independently recorded archetype control.</summary>
public sealed record PackedProfileEvidence(string SamePolicySha256, string TunedArchetypesSha256)
{
    internal void Validate()
    {
        ProfileIdentity.RequireSha256(SamePolicySha256, nameof(SamePolicySha256));
        ProfileIdentity.RequireSha256(TunedArchetypesSha256, nameof(TunedArchetypesSha256));
        if (string.Equals(SamePolicySha256, TunedArchetypesSha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Packed placement requires two distinct control receipts.");
    }
}
