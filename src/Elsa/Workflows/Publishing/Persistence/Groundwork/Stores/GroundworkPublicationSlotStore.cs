using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationSlotStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer,
    IBoundedDocumentStore queries)
    : GroundworkPublishingStore(
        store,
        serializer,
        PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
        queries ?? throw new ArgumentNullException(nameof(queries))), IPublicationSlotStore
{
    public async ValueTask<PublicationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync<SlotDocument>(PublicationSlotIdentity.Create(workflowDefinitionId, slotName), cancellationToken);
        return loaded?.Document.Slot;
    }

    public async ValueTask<IReadOnlyCollection<PublicationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        var docs = await QueryAsync<SlotDocument>(
            PublishingGroundworkStorageManifest.ListByDefinitionQuery,
            PublishingGroundworkStorageManifest.WorkflowDefinitionIdField,
            workflowDefinitionId,
            cancellationToken);
        return docs.Select(x => x.Slot).OrderBy(x => x.SlotName, StringComparer.Ordinal).ToArray();
    }

    public ValueTask<PublicationSlotTransitionResult> TryActivateAsync(
        string workflowDefinitionId, string slotName, string publicationId, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
        TransitionAsync(workflowDefinitionId, slotName, publicationId, expectedRevision, updatedAt, cancellationToken);

    public ValueTask<PublicationSlotTransitionResult> TryUnpublishAsync(
        string workflowDefinitionId, string slotName, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
        TransitionAsync(workflowDefinitionId, slotName, null, expectedRevision, updatedAt, cancellationToken);

    private async ValueTask<PublicationSlotTransitionResult> TransitionAsync(
        string workflowDefinitionId, string slotName, string? publicationId, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (publicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        var slotId = PublicationSlotIdentity.Create(workflowDefinitionId, slotName);
        var loaded = await LoadAsync<SlotDocument>(slotId, cancellationToken);
        var current = loaded?.Document.Slot ?? new PublicationSlot(slotId, workflowDefinitionId, slotName, null, 0, updatedAt);
        if (current.Revision != expectedRevision)
            return Failed(current, "slot_revision_conflict", "The publication slot revision changed.");
        if (publicationId is not null)
        {
            var owners = await QueryAsync<SlotDocument>(
                PublishingGroundworkStorageManifest.FindByActivePublicationQuery,
                PublishingGroundworkStorageManifest.ActivePublicationIdField,
                publicationId,
                cancellationToken);
            if (owners.Any(owner => !StringComparer.Ordinal.Equals(owner.Slot.SlotId, slotId)))
                return Failed(current, "publication_already_active", "The publication is already active in another slot.");
        }

        var next = current with { ActivePublicationId = publicationId, Revision = current.Revision + 1, UpdatedAt = updatedAt };
        var result = await SaveAsync(slotId, new SlotDocument(workflowDefinitionId, next), loaded?.Envelope.Version ?? 0, cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            var winner = await FindAsync(workflowDefinitionId, slotName, cancellationToken) ?? current;
            return Failed(winner, "slot_revision_conflict", "The publication slot revision changed.");
        }
        return new PublicationSlotTransitionResult(true, next, current.ActivePublicationId);
    }

    private static PublicationSlotTransitionResult Failed(PublicationSlot slot, string code, string message) =>
        new(false, slot, Failure: new PublicationFailure(code, message));

    private sealed record SlotDocument(string WorkflowDefinitionId, PublicationSlot Slot);
}
