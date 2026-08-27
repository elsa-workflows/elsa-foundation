using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Slots.Get;

[Get("/publishing/workflows/{definitionId}/slots/{slotName}")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore) : ApiEndpoint<GetPublicationSlot, PublicationSlotView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "GetPublicationSlotEndpoint";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<PublicationSlotView> HandleAsync(GetPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await slotStore.FindAsync(request.DefinitionId, request.SlotName, cancellationToken)
            ?? throw new EntityNotFoundException(
                $"Publication slot '{request.SlotName}' was not found for workflow '{request.DefinitionId}'.");
        return await PublicationSlotViews.ComposeAsync(slot, publicationStore, cancellationToken);
    }
}
