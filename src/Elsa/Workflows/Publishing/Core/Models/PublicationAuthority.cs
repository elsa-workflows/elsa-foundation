using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>The sole start authority for one named publication lane of a workflow definition.</summary>
public sealed record PublicationSlot(
    string SlotId,
    string WorkflowDefinitionId,
    string SlotName,
    string? ActivePublicationId,
    long Revision,
    DateTimeOffset UpdatedAt);

/// <summary>Creates deterministic, unambiguous slot identifiers without coupling Core to a persistence provider.</summary>
public static class PublicationSlotIdentity
{
    public static string Create(string workflowDefinitionId, string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        return $"publication-slot:{workflowDefinitionId.Length}:{workflowDefinitionId}:{slotName.Length}:{slotName}";
    }
}

public sealed record PublicationSlotTransitionResult(
    bool Succeeded,
    PublicationSlot Slot,
    string? ReplacedPublicationId = null,
    PublicationFailure? Failure = null);

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

/// <summary>One publishing request for the shared activation lifecycle.</summary>
/// <remarks>
/// The candidate is publishing's journal row; the executable and provenance-bearing source reference are what
/// <see cref="IWorkflowActivationCoordinator"/> actually needs. Publishing supplies all three and owns none of
/// the activation sequence.
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
