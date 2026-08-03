using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Prepares serving projections before selecting one revisioned slot authority.</summary>
public sealed class PublicationActivator(
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore,
    IPublicationProjectionPreparer projectionPreparer,
    TimeProvider timeProvider) : IPublicationActivator
{
    public async ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        var candidate = request.Candidate;
        ValidateCandidate(candidate);

        await publicationStore.SaveAsync(candidate, cancellationToken);

        try
        {
            await projectionPreparer.PrepareAsync(candidate, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = new PublicationFailure("projection_preparation_failed", exception.Message);
            var failed = candidate with { Status = PublicationStatus.Failed, Failure = failure };
            await TransitionOrThrowAsync(failed, PublicationStatus.Candidate, cancellationToken);
            var unchanged = await CurrentSlotAsync(candidate, cancellationToken);
            return new PublicationActivationResult(false, failed, unchanged, failure);
        }

        var now = timeProvider.GetUtcNow();
        var slotResult = await slotStore.TryActivateAsync(
            candidate.WorkflowDefinitionId,
            candidate.SlotName,
            candidate.PublicationId,
            candidate.ExpectedSlotRevision,
            now,
            cancellationToken);

        if (!slotResult.Succeeded)
        {
            var failure = slotResult.Failure ?? new PublicationFailure("slot_revision_conflict", "The publication slot revision changed.");
            var failed = candidate with { Status = PublicationStatus.Failed, Failure = failure };
            await TransitionOrThrowAsync(failed, PublicationStatus.Candidate, cancellationToken);
            return new PublicationActivationResult(false, failed, slotResult.Slot, failure, slotResult.ReplacedPublicationId);
        }

        try
        {
            await projectionPreparer.ActivateAsync(candidate, slotResult.ReplacedPublicationId, cancellationToken);
        }
        catch (Exception exception)
        {
            return await CompensateActivationFailureAsync(
                candidate,
                slotResult,
                exception,
                "projection_activation_failed");
        }

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

            if (slotResult.ReplacedPublicationId is { } replacedId &&
                !StringComparer.Ordinal.Equals(replacedId, candidate.PublicationId))
            {
                var replaced = await publicationStore.FindAsync(replacedId, cancellationToken)
                    ?? throw new InvalidOperationException($"The replaced publication '{replacedId}' does not exist.");
                var retired = replaced with { Status = PublicationStatus.Retired, RetiredAt = now };
                await TransitionOrThrowAsync(retired, PublicationStatus.Active, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            return await CompensateActivationFailureAsync(
                candidate,
                slotResult,
                exception,
                "publication_state_transition_failed");
        }

        return new PublicationActivationResult(
            true,
            active,
            slotResult.Slot,
            ReplacedPublicationId: slotResult.ReplacedPublicationId);
    }

    private async ValueTask<PublicationActivationResult> CompensateActivationFailureAsync(
        PublicationRecord candidate,
        PublicationSlotTransitionResult activatedSlot,
        Exception activationFailure,
        string failureCode)
    {
        var failure = new PublicationFailure(failureCode, SafeMessage(activationFailure));
        var authorityCompensationFailure = await CaptureFailureAsync(() =>
            CompensateAuthorityAsync(candidate, activatedSlot, CancellationToken.None));
        var projectionCompensationFailure = await CaptureFailureAsync(() =>
            projectionPreparer.CompensateAsync(candidate, activatedSlot.ReplacedPublicationId, CancellationToken.None));
        var publicationCompensationFailure = await CaptureFailureAsync(() =>
            CompensatePublicationRecordsAsync(candidate, activatedSlot.ReplacedPublicationId, failure));

        if (authorityCompensationFailure is not null ||
            projectionCompensationFailure is not null ||
            publicationCompensationFailure is not null)
        {
            failure = new PublicationFailure(
                "activation_compensation_failed",
                BuildCompensationFailureMessage(
                    activationFailure,
                    authorityCompensationFailure,
                    projectionCompensationFailure,
                    publicationCompensationFailure));
        }

        var failed = await publicationStore.FindAsync(candidate.PublicationId, CancellationToken.None)
            ?? candidate with { Status = PublicationStatus.Failed, Failure = failure };
        var restoredSlot = await CurrentSlotAsync(candidate, CancellationToken.None);
        return new PublicationActivationResult(
            false,
            failed,
            restoredSlot,
            failure,
            activatedSlot.ReplacedPublicationId);
    }

    private async ValueTask CompensatePublicationRecordsAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        PublicationFailure failure)
    {
        Exception? replacedFailure = null;
        try
        {
            if (replacedPublicationId is not null &&
                !StringComparer.Ordinal.Equals(replacedPublicationId, candidate.PublicationId))
            {
                var replaced = await publicationStore.FindAsync(replacedPublicationId, CancellationToken.None)
                    ?? throw new InvalidOperationException($"The replaced publication '{replacedPublicationId}' does not exist.");
                if (replaced.Status == PublicationStatus.Retired)
                {
                    var restored = replaced with { Status = PublicationStatus.Active, RetiredAt = null, Failure = null };
                    await TransitionOrThrowAsync(restored, PublicationStatus.Retired, CancellationToken.None);
                }
                else if (replaced.Status != PublicationStatus.Active)
                    throw new InvalidOperationException(
                        $"The replaced publication '{replacedPublicationId}' cannot be restored from status '{replaced.Status}'.");
            }
        }
        catch (Exception exception)
        {
            replacedFailure = exception;
        }

        Exception? candidateFailure = null;
        try
        {
            var currentCandidate = await publicationStore.FindAsync(candidate.PublicationId, CancellationToken.None)
                ?? throw new InvalidOperationException($"The candidate publication '{candidate.PublicationId}' does not exist.");
            if (currentCandidate.Status is PublicationStatus.Candidate or PublicationStatus.Active)
            {
                var failed = currentCandidate with
                {
                    Status = PublicationStatus.Failed,
                    ActivatedAt = null,
                    RetiredAt = null,
                    Failure = failure
                };
                await TransitionOrThrowAsync(failed, currentCandidate.Status, CancellationToken.None);
            }
            else if (currentCandidate.Status != PublicationStatus.Failed)
                throw new InvalidOperationException(
                    $"The candidate publication '{candidate.PublicationId}' cannot fail from status '{currentCandidate.Status}'.");
        }
        catch (Exception exception)
        {
            candidateFailure = exception;
        }

        if (replacedFailure is not null || candidateFailure is not null)
            throw new AggregateException(
                "Publication record compensation did not converge.",
                new[] { replacedFailure, candidateFailure }.OfType<Exception>());
    }

    private static async ValueTask<Exception?> CaptureFailureAsync(Func<ValueTask> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string BuildCompensationFailureMessage(
        Exception activationFailure,
        Exception? authorityFailure,
        Exception? projectionFailure,
        Exception? publicationFailure)
    {
        var parts = new List<string> { activationFailure.Message };
        if (authorityFailure is not null)
            parts.Add($"Authority compensation failed: {authorityFailure.Message}");
        if (projectionFailure is not null)
            parts.Add($"Projection compensation failed: {projectionFailure.Message}");
        if (publicationFailure is not null)
            parts.Add($"Publication record compensation failed: {publicationFailure.Message}");
        var message = string.Join(" ", parts);
        return message.Length <= 512 ? message : message[..512];
    }

    private static string SafeMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        return message.Length <= 512 ? message : message[..512];
    }

    private async ValueTask CompensateAuthorityAsync(
        PublicationRecord candidate,
        PublicationSlotTransitionResult activatedSlot,
        CancellationToken cancellationToken)
    {
        PublicationSlotTransitionResult compensation;
        if (activatedSlot.ReplacedPublicationId is { } replacedPublicationId)
            compensation = await slotStore.TryActivateAsync(
                candidate.WorkflowDefinitionId,
                candidate.SlotName,
                replacedPublicationId,
                activatedSlot.Slot.Revision,
                timeProvider.GetUtcNow(),
                cancellationToken);
        else
            compensation = await slotStore.TryUnpublishAsync(
                candidate.WorkflowDefinitionId,
                candidate.SlotName,
                activatedSlot.Slot.Revision,
                timeProvider.GetUtcNow(),
                cancellationToken);

        if (!compensation.Succeeded)
            throw new InvalidOperationException(
                $"Publication '{candidate.PublicationId}' projection activation failed and prior slot authority could not be restored.");
    }

    private async ValueTask<PublicationSlot> CurrentSlotAsync(
        PublicationRecord candidate,
        CancellationToken cancellationToken) =>
        await slotStore.FindAsync(candidate.WorkflowDefinitionId, candidate.SlotName, cancellationToken)
        ?? new PublicationSlot(
            candidate.SlotId,
            candidate.WorkflowDefinitionId,
            candidate.SlotName,
            ActivePublicationId: null,
            Revision: 0,
            timeProvider.GetUtcNow());

    private async ValueTask TransitionOrThrowAsync(
        PublicationRecord publication,
        PublicationStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!await publicationStore.TryTransitionAsync(publication, expectedStatus, cancellationToken))
            throw new InvalidOperationException(
                $"Publication '{publication.PublicationId}' did not transition from '{expectedStatus}' to '{publication.Status}'.");
    }

    private static void ValidateCandidate(PublicationRecord candidate)
    {
        if (candidate.Status != PublicationStatus.Candidate)
            throw new ArgumentException("Publication activation requires a Candidate record.", nameof(candidate));
        if (!StringComparer.Ordinal.Equals(
                candidate.SlotId,
                PublicationSlotIdentity.Create(candidate.WorkflowDefinitionId, candidate.SlotName)))
            throw new ArgumentException("The publication slot identity does not match its definition and slot name.", nameof(candidate));
    }
}
