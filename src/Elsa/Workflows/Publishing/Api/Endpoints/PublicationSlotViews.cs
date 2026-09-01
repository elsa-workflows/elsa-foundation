using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// Composes the slot view with the publication a reader should see: the active publication when one
/// exists, otherwise the most recent record for the slot. Shared by every slot-shaped endpoint.
/// </summary>
internal static class PublicationSlotViews
{
    /// <summary>
    /// The slot lifecycle handlers report a missing or unrestorable slot through
    /// <see cref="InvalidOperationException"/> messages; only the two lifecycle endpoints translate
    /// those to 404, exactly as the hand-written handlers did.
    /// </summary>
    public static bool IsMissingSlot(InvalidOperationException exception) =>
        exception.Message.Contains("does not exist", StringComparison.Ordinal) ||
        exception.Message.Contains("no retired publication", StringComparison.Ordinal) ||
        exception.Message.Contains("unavailable", StringComparison.Ordinal) ||
        exception.Message.Contains("no source reference", StringComparison.Ordinal);

    public static async ValueTask<PublicationSlotView> ComposeAsync(
        PublicationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken) =>
        PublicationSlotView.From(slot, await ResolveVisiblePublicationAsync(slot, publicationStore, cancellationToken));

    public static async ValueTask<PublicationSlotView> ComposeAsync(
        WorkflowActivationSlot slot,
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

    private static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        WorkflowActivationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        if (slot.ActiveActivationId is { } activeActivationId)
            return await publicationStore.FindAsync(activeActivationId, cancellationToken);
        return (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .OrderByDescending(publication => publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
