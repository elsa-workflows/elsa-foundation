using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>One publication attempt and its controlled lifecycle facts.</summary>
public sealed record PublicationRecord(
    string PublicationId,
    string SlotId,
    string WorkflowDefinitionId,
    string WorkflowDefinitionVersionId,
    string ArtifactId,
    string? SourceReferenceId,
    long ExpectedSlotRevision,
    PublicationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RetiredAt,
    PublicationFailure? Failure,
    string SlotName = "default");

/// <summary>
/// One publishing request for the shared activation lifecycle.
/// </summary>
/// <remarks>
/// The candidate is publishing's journal row; the executable and the provenance-bearing reference are what
/// <c>IWorkflowActivationCoordinator</c> actually needs. Publishing supplies all three and owns none of the
/// sequence (FR-B-006).
/// </remarks>
public sealed record PublicationActivationRequest(
    PublicationRecord Candidate,
    WorkflowExecutable Executable,
    WorkflowExecutableSourceReference Reference);

public sealed record PublicationActivationResult(
    bool Succeeded,
    PublicationRecord Publication,
    WorkflowActivationSlot Slot,
    PublicationFailure? Failure = null,
    string? ReplacedPublicationId = null);
