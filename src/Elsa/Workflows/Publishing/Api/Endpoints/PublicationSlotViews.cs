using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// Composes the slot view with the publication a reader should see: the active publication when one
/// exists, otherwise the most recent record for the slot. Shared by every slot-shaped endpoint.
/// </summary>
internal static class PublicationSlotViews
{
    public static async ValueTask<PublicationSlotView> ComposeAsync(
        PublicationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken) =>
        PublicationSlotView.From(slot, await ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken));

    private static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        PublicationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        if (slot.ActivePublicationId is { } activePublicationId)
            return await publicationStore.FindAsync(activePublicationId, cancellationToken);
        return (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .OrderByDescending(publication => publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
