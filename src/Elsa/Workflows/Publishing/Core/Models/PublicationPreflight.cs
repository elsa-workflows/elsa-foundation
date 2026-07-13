namespace Elsa.Workflows.Publishing.Core.Models;

public enum PublicationTriggerCardinality
{
    Exclusive,
    FanOut
}

public enum PublicationTriggerChangeKind
{
    Added,
    Removed,
    Retained
}

public sealed record PublicationTriggerClaim(
    string ClaimId,
    string PublicationId,
    string ArtifactId,
    string ExecutableNodeId,
    string StimulusType,
    string StimulusHash,
    PublicationTriggerCardinality Cardinality,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PublicationAuthoritativeClaimSet(
    string PublicationId,
    string SlotName,
    bool IsReplacedPublication,
    IReadOnlyCollection<PublicationTriggerClaim> Claims);

public sealed record PublicationTriggerChange(
    string StimulusType,
    string StimulusHash,
    PublicationTriggerChangeKind Change,
    PublicationTriggerClaim? CandidateClaim = null,
    PublicationTriggerClaim? ExistingClaim = null);

public sealed record PublicationTriggerConflict(
    string PublicationId,
    string SlotName,
    string StimulusType,
    string StimulusHash,
    PublicationTriggerCardinality Cardinality,
    string ClaimId);

public sealed record PublicationPreflightResult(
    bool CanActivate,
    IReadOnlyCollection<PublicationTriggerChange> Changes,
    IReadOnlyCollection<PublicationTriggerConflict> Conflicts);

