using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class UnpublishPublicationSlotRequestHandler(
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore,
    IPublicationProjectionPreparer projectionPreparer,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider) : IRequestHandler<UnpublishPublicationSlot, PublicationSlot>
{
    public async Task<PublicationSlot> Handle(UnpublishPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await slotStore.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActivePublicationId is not { } publicationId)
            return slot;
        var publication = await publicationStore.FindAsync(publicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Active publication '{publicationId}' does not exist.");
        var now = timeProvider.GetUtcNow();
        var transition = await slotStore.TryUnpublishAsync(
            request.WorkflowDefinitionId,
            request.SlotName,
            slot.Revision,
            now,
            cancellationToken);
        if (!transition.Succeeded)
            throw new PublicationActivationException(transition.Failure);

        try
        {
            await projectionPreparer.RemoveAsync(publication, cancellationToken);
        }
        catch (Exception removalException)
        {
            Exception? compensationException = null;
            try
            {
                var compensation = await slotStore.TryActivateAsync(
                    request.WorkflowDefinitionId,
                    request.SlotName,
                    publication.PublicationId,
                    transition.Slot.Revision,
                    timeProvider.GetUtcNow(),
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
            await sourceReferenceStore.RetireAsync(sourceReferenceId, now, "publication-unpublished", cancellationToken);
        return transition.Slot;
    }
}

public sealed class RestorePublicationSlotRequestHandler(
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore,
    IPublicationActivator activator,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
    TimeProvider timeProvider) : IRequestHandler<RestorePublicationSlot, PublicationSlot>
{
    public async Task<PublicationSlot> Handle(RestorePublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await slotStore.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActivePublicationId is not null)
            return slot;

        var prior = (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .Where(publication => publication.Status == PublicationStatus.Retired)
            .OrderByDescending(publication => publication.RetiredAt ?? publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' has no retired publication to restore.");
        _ = await executableStore.FindAsync(prior.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Executable artifact '{prior.ArtifactId}' is unavailable for restore.");

        var priorReference = prior.SourceReferenceId is { } priorReferenceId
            ? await sourceReferenceStore.FindAsync(priorReferenceId, cancellationToken)
            : null;
        var now = timeProvider.GetUtcNow();
        var publicationId = $"publication-{ShortIdentityGenerator.Generate(now)}";
        var sourceReferenceId = ShortIdentityGenerator.Generate(now);
        var candidate = prior with
        {
            PublicationId = publicationId,
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
                PublicationId = publicationId,
                SlotId = slot.SlotId
            };

        PublicationActivationResult? activation = null;
        await rootWriteLeaseManager.ExecuteAsync(
            prior.ArtifactId,
            $"restore:{publicationId}",
            async ct =>
            {
                await sourceReferenceStore.SaveAsync(reference, ct);
                try
                {
                    activation = await activator.ActivateAsync(new PublicationActivationRequest(candidate), ct);
                    if (!activation.Succeeded)
                        throw new PublicationActivationException(activation.Failure);
                }
                catch
                {
                    await sourceReferenceStore.RetireAsync(
                        reference.SourceReferenceId,
                        timeProvider.GetUtcNow(),
                        "publication-restore-failed",
                        CancellationToken.None);
                    throw;
                }
            },
            cancellationToken);
        return activation!.Slot;
    }
}
