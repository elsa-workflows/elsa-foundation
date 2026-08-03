using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Computes the trigger projection diff and cross-slot cardinality conflicts before activation.</summary>
public sealed class PublicationPreflightService : IPublicationPreflightService
{
    public PublicationPreflightResult Evaluate(
        IReadOnlyCollection<PublicationTriggerClaim> candidateClaims,
        IReadOnlyCollection<PublicationAuthoritativeClaimSet> authoritativeClaims)
    {
        ArgumentNullException.ThrowIfNull(candidateClaims);
        ArgumentNullException.ThrowIfNull(authoritativeClaims);

        var replacedClaims = authoritativeClaims
            .Where(set => set.IsReplacedPublication)
            .SelectMany(set => set.Claims)
            .ToArray();
        var changes = BuildChanges(candidateClaims, replacedClaims);

        var conflicts = authoritativeClaims
            .Where(set => !set.IsReplacedPublication)
            .SelectMany(set => set.Claims.Select(claim => (Set: set, Claim: claim)))
            .Where(existing => candidateClaims.Any(candidate =>
                HasSameStimulus(candidate, existing.Claim) &&
                (candidate.Cardinality == PublicationTriggerCardinality.Exclusive ||
                 existing.Claim.Cardinality == PublicationTriggerCardinality.Exclusive)))
            .OrderBy(existing => existing.Set.SlotName, StringComparer.Ordinal)
            .ThenBy(existing => existing.Set.PublicationId, StringComparer.Ordinal)
            .ThenBy(existing => existing.Claim.ClaimId, StringComparer.Ordinal)
            .Select(existing => new PublicationTriggerConflict(
                existing.Set.PublicationId,
                existing.Set.SlotName,
                existing.Claim.StimulusType,
                existing.Claim.StimulusHash,
                existing.Claim.Cardinality,
                existing.Claim.ClaimId))
            .ToArray();

        return new PublicationPreflightResult(conflicts.Length == 0, changes, conflicts);
    }

    private static IReadOnlyCollection<PublicationTriggerChange> BuildChanges(
        IReadOnlyCollection<PublicationTriggerClaim> candidates,
        IReadOnlyCollection<PublicationTriggerClaim> existing)
    {
        var candidateGroups = candidates.GroupBy(StimulusKey.From).ToDictionary(group => group.Key);
        var existingGroups = existing.GroupBy(StimulusKey.From).ToDictionary(group => group.Key);
        var changes = new List<PublicationTriggerChange>();

        foreach (var key in candidateGroups.Keys
                     .Union(existingGroups.Keys)
                     .OrderBy(item => item.StimulusType, StringComparer.Ordinal)
                     .ThenBy(item => item.StimulusHash, StringComparer.Ordinal))
        {
            var candidateClaims = candidateGroups.GetValueOrDefault(key)?.OrderBy(claim => claim.ClaimId, StringComparer.Ordinal).ToArray() ?? [];
            var existingClaims = existingGroups.GetValueOrDefault(key)?.OrderBy(claim => claim.ClaimId, StringComparer.Ordinal).ToArray() ?? [];
            var retainedCount = Math.Min(candidateClaims.Length, existingClaims.Length);

            for (var index = 0; index < retainedCount; index++)
                changes.Add(new PublicationTriggerChange(key.StimulusType, key.StimulusHash, PublicationTriggerChangeKind.Retained, candidateClaims[index], existingClaims[index]));
            for (var index = retainedCount; index < candidateClaims.Length; index++)
                changes.Add(new PublicationTriggerChange(key.StimulusType, key.StimulusHash, PublicationTriggerChangeKind.Added, candidateClaims[index]));
            for (var index = retainedCount; index < existingClaims.Length; index++)
                changes.Add(new PublicationTriggerChange(key.StimulusType, key.StimulusHash, PublicationTriggerChangeKind.Removed, ExistingClaim: existingClaims[index]));
        }

        return changes;
    }

    private static bool HasSameStimulus(PublicationTriggerClaim left, PublicationTriggerClaim right) =>
        StringComparer.Ordinal.Equals(left.StimulusType, right.StimulusType) &&
        StringComparer.Ordinal.Equals(left.StimulusHash, right.StimulusHash);

    private readonly record struct StimulusKey(string StimulusType, string StimulusHash)
    {
        public static StimulusKey From(PublicationTriggerClaim claim) =>
            new(claim.StimulusType, claim.StimulusHash);
    }
}
