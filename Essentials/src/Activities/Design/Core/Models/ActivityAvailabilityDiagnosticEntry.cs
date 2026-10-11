namespace Elsa.Activities.Design.Core.Models;

public sealed record ActivityAvailabilityDiagnosticEntry(
    string? ActivityDefinitionId,
    string? ActivityTypeKey,
    string? DisplayName,
    string? Category,
    ActivityAvailabilityDiagnosticState State,
    ActivityAvailabilityPolicyLayer Layer,
    ActivityAvailabilityReferenceKind ReferenceKind,
    string? ReferenceName,
    string Reason);
