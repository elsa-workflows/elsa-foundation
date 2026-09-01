using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Publishing's <see cref="PublicationRecord"/> bookkeeping wrapped around one call to the shared
/// <see cref="IWorkflowActivationCoordinator"/>.
/// </summary>
/// <remarks>
/// Leases, source references, serving projections, slot CAS, observer notification and compensation all belong to
/// the runtime coordinator. This type owns only the publication journal and maps runtime outcomes to publishing's
/// failure vocabulary.
/// </remarks>
public sealed class PublicationActivator(
    IWorkflowActivationCoordinator activationCoordinator,
    IPublicationRecordStore publicationStore,
    TimeProvider timeProvider,
    ILogger<PublicationActivator>? logger = null) : IPublicationActivator
{
    /// <summary>The activation source every publish-pipeline request is owned by.</summary>
    public static WorkflowActivationSource Source => WorkflowActivationSource.Publishing;

    public async ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentNullException.ThrowIfNull(request.Executable);
        ArgumentNullException.ThrowIfNull(request.Reference);
        var candidate = request.Candidate;
        ValidateCandidate(candidate);

        await publicationStore.SaveAsync(candidate, cancellationToken);

        WorkflowActivationResult activation;
        try
        {
            activation = await activationCoordinator.ActivateAsync(
                new WorkflowActivationCommand(
                    request.Executable,
                    request.Reference,
                    candidate.SlotName,
                    candidate.PublicationId,
                    Source,
                    candidate.ExpectedSlotRevision),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkflowActivationException exception)
        {
            // The coordinator refused to run the lifecycle. Its own writes did not run, so the journal candidate can
            // still be marked failed while the coordinator's domain exception continues to the caller.
            await FailCandidateAsync(
                candidate,
                new PublicationFailure("publication_activation_refused", SafeMessage(exception.Message)),
                CancellationToken.None);
            throw;
        }

        if (!activation.Succeeded)
        {
            var failure = MapFailure(activation);
            var failed = await FailCandidateAsync(candidate, failure, cancellationToken);
            return new PublicationActivationResult(
                false,
                failed,
                activation.Slot,
                failure,
                activation.ReplacedActivationId);
        }

        var now = timeProvider.GetUtcNow();
        var active = candidate with
        {
            Status = PublicationStatus.Active,
            ActivatedAt = now,
            RetiredAt = null,
            Failure = null
        };
        try
        {
            await TransitionOrThrowAsync(active, PublicationStatus.Candidate, cancellationToken);
            await RetireReplacedRecordAsync(candidate, activation.ReplacedActivationId, now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The slot has already flipped and serving projections are live. A journal failure must not roll back
            // serving; the slot is runtime authority and the journal is reconciled separately.
            logger?.LogError(
                exception,
                "Publication {PublicationId} of workflow definition {DefinitionId} slot {SlotName} is active, but its publication journal could not be updated to match.",
                candidate.PublicationId,
                candidate.WorkflowDefinitionId,
                candidate.SlotName);
        }

        return new PublicationActivationResult(
            true,
            active,
            activation.Slot,
            ReplacedPublicationId: activation.ReplacedActivationId);
    }

    private static PublicationFailure MapFailure(WorkflowActivationResult activation) => activation.Conflict switch
    {
        WorkflowActivationConflict.RevisionMismatch =>
            new("slot_revision_conflict", activation.Diagnostic ?? "The publication slot revision changed."),
        WorkflowActivationConflict.ForeignSource =>
            new("slot_owner_conflict", activation.Diagnostic ?? "The activation slot is owned by another activation source."),
        _ when activation.CompensationDiagnostic is not null =>
            new("activation_compensation_failed", activation.Diagnostic ?? "Publication activation failed and its compensation did not converge."),
        _ => new(MapFailedStep(activation.FailedStep), activation.Diagnostic ?? "Publication activation failed.")
    };

    private static string MapFailedStep(WorkflowActivationStep step) => step switch
    {
        WorkflowActivationStep.ProjectionPreparation => "projection_preparation_failed",
        WorkflowActivationStep.ProjectionActivation or WorkflowActivationStep.TriggerObserverNotification =>
            "projection_activation_failed",
        _ => "publication_activation_failed"
    };

    private async ValueTask<PublicationRecord> FailCandidateAsync(
        PublicationRecord candidate,
        PublicationFailure failure,
        CancellationToken cancellationToken)
    {
        var current = await publicationStore.FindAsync(candidate.PublicationId, cancellationToken) ?? candidate;
        if (current.Status is not (PublicationStatus.Candidate or PublicationStatus.Active))
            return current;

        var failed = current with
        {
            Status = PublicationStatus.Failed,
            ActivatedAt = null,
            RetiredAt = null,
            Failure = failure
        };
        await TransitionOrThrowAsync(failed, current.Status, cancellationToken);
        return failed;
    }

    private async ValueTask RetireReplacedRecordAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (replacedPublicationId is not { } replacedId ||
            StringComparer.Ordinal.Equals(replacedId, candidate.PublicationId))
            return;

        var replaced = await publicationStore.FindAsync(replacedId, cancellationToken)
            ?? throw new InvalidOperationException($"The replaced publication '{replacedId}' does not exist.");
        var retired = replaced with { Status = PublicationStatus.Retired, RetiredAt = now };
        await TransitionOrThrowAsync(retired, PublicationStatus.Active, cancellationToken);
    }

    private async ValueTask TransitionOrThrowAsync(
        PublicationRecord publication,
        PublicationStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!await publicationStore.TryTransitionAsync(publication, expectedStatus, cancellationToken))
            throw new InvalidOperationException(
                $"Publication '{publication.PublicationId}' did not transition from '{expectedStatus}' to '{publication.Status}'.");
    }

    private static string SafeMessage(string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "Publication activation failed."
            : message.Length <= 512 ? message : message[..512];

    private static void ValidateCandidate(PublicationRecord candidate)
    {
        if (candidate.Status != PublicationStatus.Candidate)
            throw new ArgumentException("Publication activation requires a Candidate record.", nameof(candidate));
        if (!StringComparer.Ordinal.Equals(
                candidate.SlotId,
                WorkflowActivationSlotIdentity.Create(candidate.WorkflowDefinitionId, candidate.SlotName)))
            throw new ArgumentException("The publication slot identity does not match its definition and slot name.", nameof(candidate));
    }
}
