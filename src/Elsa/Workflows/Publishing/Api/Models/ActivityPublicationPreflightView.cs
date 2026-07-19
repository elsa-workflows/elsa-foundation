using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record ActivityPublicationDependencyEvidenceView(
    string DefinitionId,
    string VersionId,
    string Version,
    string TemplateHash,
    string OccurrenceId);

public sealed record ActivityPublicationCapabilityReadinessView(
    string Kind,
    string Key,
    string? SchemaVersion,
    string Status,
    IReadOnlyList<string> SupportedSchemaVersions);

public sealed record ActivityPublicationPreflightView(
    string DraftId,
    long DraftRevision,
    string DefinitionId,
    string? DefinitionHeadVersionId,
    bool HasBaseline,
    string ReviewToken,
    bool IsPublishable,
    string MinimumVersion,
    IReadOnlyList<string> ValidVersions,
    ActivityVersionDiff? Diff,
    IReadOnlyList<ActivityVersionChange> ImpactFirstChanges,
    IReadOnlyList<ActivityPublicationDependencyEvidenceView> Dependencies,
    ActivityPublicationCapabilityReadinessView Provider,
    IReadOnlyList<ActivityPublicationCapabilityReadinessView> Storage,
    IReadOnlyList<ActivityPublicationCapabilityReadinessView> Runtime,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityPublicationReceiptView(
    string IdempotencyKey,
    string Status,
    string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string ReviewToken,
    string RequestedVersion,
    ActivityPublicationOutcome? Outcome,
    string? ErrorCode,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    DateTimeOffset UpdatedAt)
{
    public static ActivityPublicationReceiptView From(ActivityPublicationReceipt receipt) => new(
        receipt.IdempotencyKey,
        receipt.Status.ToString(),
        receipt.DraftId,
        receipt.ExpectedDraftRevision,
        receipt.ExpectedDefinitionHeadVersionId,
        receipt.ReviewToken,
        receipt.RequestedVersion,
        receipt.Outcome,
        receipt.ErrorCode,
        receipt.Diagnostics,
        receipt.UpdatedAt);
}
