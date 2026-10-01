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

/// <summary>The outcome of completing one publication slot (<c>IPublicationActivator.CompleteAsync</c>).</summary>
/// <param name="Succeeded">False only when the runtime could not complete the slot's activation.</param>
/// <param name="Slot">The slot as completion found it.</param>
/// <param name="Publication">
/// The record of the publication the slot names, as the journal holds it afterwards; <see langword="null"/> when the slot
/// names none of publishing's publications.
/// </param>
/// <param name="Failure">Why the slot's activation could not be completed.</param>
public sealed record PublicationCompletionResult(
    bool Succeeded,
    WorkflowActivationSlot Slot,
    PublicationRecord? Publication = null,
    PublicationFailure? Failure = null);
