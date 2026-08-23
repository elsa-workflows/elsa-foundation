using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Slots.Unpublish;

[Delete("/publishing/workflows/{definitionId}/slots/{slotName}")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(
    IRequestSender sender,
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
        var slot = await sender.Send(new UnpublishPublicationSlot(request.DefinitionId, request.SlotName), cancellationToken);
        return await PublicationSlotViews.ComposeAsync(slot, publicationStore, cancellationToken);
    }
}
