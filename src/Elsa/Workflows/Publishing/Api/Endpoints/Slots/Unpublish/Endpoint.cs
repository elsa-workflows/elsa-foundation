using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Slots.Unpublish;

[Delete("/publishing/workflows/{definitionId}/slots/{slotName}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(
    IPublicationSlotUnpublisher unpublisher,
    IPublicationRecordStore publicationStore) : ApiEndpoint<UnpublishPublicationSlotRequest, PublicationSlotView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UnpublishPublicationSlotEndpoint";
        options.Accepts = ["*/*", "application/json"];
        // The published operation reads nothing from the request body.
        options.BodyMode = EndpointBodyMode.None;
    }

    public override async Task<PublicationSlotView> HandleAsync(UnpublishPublicationSlotRequest request, CancellationToken cancellationToken)
    {
        Core.Models.PublicationSlot slot;
        try
        {
            slot = await unpublisher.UnpublishAsync(request.DefinitionId, request.SlotName, cancellationToken);
        }
        catch (InvalidOperationException exception) when (PublicationSlotViews.IsMissingSlot(exception))
        {
            throw new EntityNotFoundException(exception.Message);
        }

        return await PublicationSlotViews.ComposeAsync(slot, publicationStore, cancellationToken);
    }
}
