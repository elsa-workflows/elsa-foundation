using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public enum ActivityPublicationReceiptStatus
{
    Applied,
    Rejected,
    Stale,
    Failed,
    OutcomeUnknown
}

public sealed record ActivityPublicationOutcome(
    string DefinitionId,
    string DefinitionVersionId,
    string DraftId,
    string Version,
    string TemplateId,
    string TemplateHash,
    string SourceReferenceId,
    DateTimeOffset PublishedAt);

/// <summary>
/// Durable terminal outcome for one idempotent activity-publication request. The request fingerprint
/// prevents one operation identity from being reused for different reviewed material.
/// </summary>
public sealed record ActivityPublicationReceipt(
    string? TenantId,
    string IdempotencyKey,
    string RequestFingerprint,
    ActivityPublicationReceiptStatus Status,
    string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string ReviewToken,
    string RequestedVersion,
    ActivityPublicationOutcome? Outcome,
    string? ErrorCode,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    DateTimeOffset UpdatedAt);
