using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using MinimalEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Slots.UnpublishReviewed;

[Post("/publishing/workflows/{definitionId}/slots/{slotName}/unpublish/{expectedRevision}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(
    IPublicationSlotUnpublisher unpublisher,
    IPublicationRecordStore publicationStore) : ApiEndpoint<UnpublishPublicationSlotAtRevisionRequest, PublicationSlotView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UnpublishPublicationSlotAtRevisionEndpoint";
        options.BodyMode = EndpointBodyMode.None;
    }

    public override async Task<PublicationSlotView> HandleAsync(UnpublishPublicationSlotAtRevisionRequest request, CancellationToken cancellationToken)
    {
        WorkflowActivationSlot slot;
        try
        {
            slot = await unpublisher.UnpublishAtRevisionAsync(request.DefinitionId, request.SlotName, request.ExpectedRevision, cancellationToken);
        }
        catch (PublicationSlotNotFoundException exception)
        {
            throw new EntityNotFoundException(exception.Message);
        }

        return await PublicationSlotViews.ComposeAsync(slot, publicationStore, cancellationToken);
    }
}