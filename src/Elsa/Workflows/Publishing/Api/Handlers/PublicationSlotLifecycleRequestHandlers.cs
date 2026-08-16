using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class UnpublishPublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore,
    IPublicationProjectionPreparer projectionPreparer,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<UnpublishPublicationSlotRequestHandler>? logger = null) : IRequestHandler<UnpublishPublicationSlot, WorkflowActivationSlot>
{
    public async Task<WorkflowActivationSlot> Handle(UnpublishPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not { } publicationId)
            return slot;
        var publication = await publicationStore.FindAsync(publicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Active publication '{publicationId}' does not exist.");
        var now = timeProvider.GetUtcNow();
        var transition = await activationAuthority.TryDeactivateAsync(
            request.WorkflowDefinitionId,
            request.SlotName,
            PublicationActivator.Source,
            slot.Revision,
            now,
            cancellationToken);
        if (!transition.Succeeded)
            throw new PublicationActivationException(ToFailure(transition));

        try
        {
            await projectionPreparer.RemoveAsync(publication, cancellationToken);
        }
        catch (Exception removalException)
        {
            Exception? compensationException = null;
            try
            {
                var compensation = await activationAuthority.TryActivateAsync(
                    new WorkflowActivationSlotRequest(
                        request.WorkflowDefinitionId,
                        request.SlotName,
                        publication.PublicationId,
                        PublicationActivator.Source,
                        transition.Slot.Revision,
                        timeProvider.GetUtcNow()),
                    CancellationToken.None);
                if (!compensation.Succeeded)
                    throw new InvalidOperationException(
                        $"Publication slot '{request.SlotName}' authority could not be restored after projection removal failed.");

                await projectionPreparer.RestoreAsync(publication, CancellationToken.None);
            }
            catch (Exception exception)
            {
                compensationException = exception;
            }

            if (compensationException is not null)
                throw new InvalidOperationException(
                    $"Publication '{publication.PublicationId}' could not be unpublished and compensation failed: {compensationException.Message}",
                    removalException);
            throw new InvalidOperationException(
                $"Publication '{publication.PublicationId}' could not be unpublished; its slot authority and serving projections were restored.",
                removalException);
        }
        var retired = publication with { Status = PublicationStatus.Retired, RetiredAt = now };
        if (!await publicationStore.TryTransitionAsync(retired, PublicationStatus.Active, cancellationToken))
            throw new InvalidOperationException($"Publication '{publicationId}' could not be retired.");
        if (publication.SourceReferenceId is { } sourceReferenceId)
        {
            logger?.LogInformation(
                "Unpublish: retiring source reference {SourceReferenceId} of workflow definition {DefinitionId} because publication {PublicationId} was unpublished from slot {SlotName}",
                sourceReferenceId,
                publication.WorkflowDefinitionId,
                publicationId,
                request.SlotName);
            await sourceReferenceStore.RetireAsync(sourceReferenceId, now, "publication-unpublished", cancellationToken);
        }
        return transition.Slot;
    }

    internal static PublicationFailure ToFailure(WorkflowActivationTransition transition) => transition.Conflict switch
    {
        WorkflowActivationConflict.ForeignSource =>
            new("slot_owner_conflict", transition.Diagnostic ?? "The activation slot is owned by another activation source."),
        _ => new("slot_revision_conflict", transition.Diagnostic ?? "The publication slot revision changed.")
    };
}

public sealed class RestorePublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore,
    IPublicationActivator activator,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<RestorePublicationSlotRequestHandler>? logger = null) : IRequestHandler<RestorePublicationSlot, WorkflowActivationSlot>
{
    public async Task<WorkflowActivationSlot> Handle(RestorePublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not null)
            return slot;

        var prior = (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .Where(publication => publication.Status == PublicationStatus.Retired)
            .OrderByDescending(publication => publication.RetiredAt ?? publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' has no retired publication to restore.");
        var executable = await executableStore.FindAsync(prior.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Executable artifact '{prior.ArtifactId}' is unavailable for restore.");

        var priorReference = prior.SourceReferenceId is { } priorReferenceId
            ? await sourceReferenceStore.FindAsync(priorReferenceId, cancellationToken)
            : null;
        var now = timeProvider.GetUtcNow();
        var publicationId = $"publication-{ShortIdentityGenerator.Generate(now)}";
        // The coordinator owns activation↔reference identity; mint the same id here so the publication record
        // points at the reference that will actually be stored.
        var sourceReferenceId = WorkflowActivationReferenceIdentity.Create(publicationId);
        var candidate = prior with
        {
            PublicationId = publicationId,
            SlotId = slot.SlotId,
            SourceReferenceId = sourceReferenceId,
            ExpectedSlotRevision = slot.Revision,
            Status = PublicationStatus.Candidate,
            CreatedAt = now,
            ActivatedAt = null,
            RetiredAt = null,
            Failure = null
        };
        var reference = priorReference is null
            ? throw new InvalidOperationException($"Publication '{prior.PublicationId}' has no source reference to restore.")
            : priorReference with
            {
                SourceReferenceId = sourceReferenceId,
                CreatedAt = now,
                PublishedAt = now,
                DeletedAt = null,
                DeletedReason = null,
                ActivationId = publicationId,
                SlotId = slot.SlotId
            };

        // Lease, reference save, projections and compensation all belong to IWorkflowActivationCoordinator, which
        // the activator calls. Restore requests activation; it does not implement it.
        var activation = await activator.ActivateAsync(
            new PublicationActivationRequest(candidate, executable, reference),
            cancellationToken);
        if (!activation.Succeeded)
        {
            logger?.LogWarning(
                "Restore: activation of publication {PublicationId} for workflow definition {DefinitionId} failed with '{FailureCode}'",
                publicationId,
                candidate.WorkflowDefinitionId,
                activation.Failure?.Code);
            throw new PublicationActivationException(activation.Failure);
        }

        return activation.Slot;
    }
}
