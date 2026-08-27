using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.Slots.List;

[Get("/publishing/workflows/{definitionId}/slots")]
[RequirePermission(WorkflowPublishingPermissions.Read)]
public sealed class Endpoint(
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore) : ApiEndpoint<ListPublicationSlots, PublicationSlotListResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ListPublicationSlotsEndpoint";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<PublicationSlotListResponse> HandleAsync(ListPublicationSlots request, CancellationToken cancellationToken)
    {
        var slots = await slotStore.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        var views = new List<PublicationSlotView>(slots.Count);
        foreach (var slot in slots)
            views.Add(await PublicationSlotViews.ComposeAsync(slot, publicationStore, cancellationToken));
        return new PublicationSlotListResponse(views);
    }
}
