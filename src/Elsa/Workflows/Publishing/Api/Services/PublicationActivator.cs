using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

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
            var failure = new PublicationFailure("projection_activation_failed", exception.Message);
            Exception? authorityCompensationFailure = null;
            try
            {
                await CompensateAuthorityAsync(candidate, slotResult, CancellationToken.None);
            }
            catch (Exception compensationException)
            {
                authorityCompensationFailure = compensationException;
            }

            Exception? projectionCompensationFailure = null;
            try
            {
                await projectionPreparer.CompensateAsync(candidate, slotResult.ReplacedPublicationId, CancellationToken.None);
            }
            catch (Exception compensationException)
            {
                projectionCompensationFailure = compensationException;
            }

            if (authorityCompensationFailure is not null || projectionCompensationFailure is not null)
                failure = new PublicationFailure(
                    "projection_compensation_failed",
                    BuildCompensationFailureMessage(exception, authorityCompensationFailure, projectionCompensationFailure));

            var failed = candidate with { Status = PublicationStatus.Failed, Failure = failure };
            await TransitionOrThrowAsync(failed, PublicationStatus.Candidate, CancellationToken.None);
            var restoredSlot = await CurrentSlotAsync(candidate, CancellationToken.None);
            return new PublicationActivationResult(false, failed, restoredSlot, failure, slotResult.ReplacedPublicationId);
        }

        var active = candidate with
        {
            Status = PublicationStatus.Active,
            ActivatedAt = now,
            RetiredAt = null,
            Failure = null
        };
        await TransitionOrThrowAsync(active, PublicationStatus.Candidate, cancellationToken);

        if (slotResult.ReplacedPublicationId is { } replacedId &&
            !StringComparer.Ordinal.Equals(replacedId, candidate.PublicationId))
        {
            var replaced = await publicationStore.FindAsync(replacedId, cancellationToken)
                ?? throw new InvalidOperationException($"The replaced publication '{replacedId}' does not exist.");
            var retired = replaced with { Status = PublicationStatus.Retired, RetiredAt = now };
            await TransitionOrThrowAsync(retired, PublicationStatus.Active, cancellationToken);
        }

        return new PublicationActivationResult(
            true,
            active,
            slotResult.Slot,
            ReplacedPublicationId: slotResult.ReplacedPublicationId);
    }

    private static string BuildCompensationFailureMessage(
        Exception activationFailure,
        Exception? authorityFailure,
        Exception? projectionFailure)
    {
        var parts = new List<string> { activationFailure.Message };
        if (authorityFailure is not null)
            parts.Add($"Authority compensation failed: {authorityFailure.Message}");
        if (projectionFailure is not null)
            parts.Add($"Projection compensation failed: {projectionFailure.Message}");
        var message = string.Join(" ", parts);
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
