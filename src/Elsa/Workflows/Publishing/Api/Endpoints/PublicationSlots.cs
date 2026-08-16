using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class ListPublicationSlotsEndpoint(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore)
    : ElsaEndpoint<ListPublicationSlots, PublicationSlotListResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.WorkflowSlots);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(ListPublicationSlots request, CancellationToken cancellationToken)
    {
        var slots = await activationAuthority.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        var views = new List<PublicationSlotView>(slots.Count);
        foreach (var slot in slots)
            views.Add(PublicationSlotView.From(slot, await ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken)));
        await Send.OkAsync(new PublicationSlotListResponse(views), cancellationToken);
    }

    internal static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        WorkflowActivationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        if (slot.ActiveActivationId is { } activePublicationId)
            return await publicationStore.FindAsync(activePublicationId, cancellationToken);

        return (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

internal sealed class GetPublicationSlotEndpoint(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore)
    : ElsaEndpoint<GetPublicationSlot, PublicationSlotView>
{
    public override void Configure()
    {
        Get(RouteConstants.WorkflowSlot);
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(GetPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.DefinitionId, request.SlotName, cancellationToken);
        if (slot is null)
        {
            ThrowError($"Publication slot '{request.SlotName}' was not found for workflow '{request.DefinitionId}'.", 404);
            return;
        }

        var publication = await ListPublicationSlotsEndpoint.ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken);
        await Send.OkAsync(PublicationSlotView.From(slot, publication), cancellationToken);
    }
}
