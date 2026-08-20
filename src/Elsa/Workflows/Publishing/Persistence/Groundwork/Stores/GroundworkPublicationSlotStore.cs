using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationSlotStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string? targetName = null)
    : GroundworkPublishingStore(
        sessions,
        accessContextAccessor,
        serializer,
        PublishingGroundworkStorageManifest.PublicationSlotDocumentKind,
        targetName),
        IPublicationSlotStore
{
    public ValueTask<PublicationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<SlotDocument>(PublicationSlotIdentity.Create(workflowDefinitionId, slotName));
        return ValueTask.FromResult(loaded?.Document.Slot);
    }

    public ValueTask<IReadOnlyCollection<PublicationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();
        var docs = QueryBy<SlotDocument>(
            PublishingGroundworkStorageManifest.WorkflowDefinitionIdField,
            workflowDefinitionId,
            PublishingGroundworkStorageManifest.SlotByDefinitionIndex);
        return ValueTask.FromResult<IReadOnlyCollection<PublicationSlot>>(
            docs.Select(x => x.Slot).OrderBy(x => x.SlotName, StringComparer.Ordinal).ToArray());
    }

    public ValueTask<PublicationSlotTransitionResult> TryActivateAsync(
        string workflowDefinitionId, string slotName, string publicationId, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
        TransitionAsync(workflowDefinitionId, slotName, publicationId, expectedRevision, updatedAt, cancellationToken);

    public ValueTask<PublicationSlotTransitionResult> TryUnpublishAsync(
        string workflowDefinitionId, string slotName, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
        TransitionAsync(workflowDefinitionId, slotName, null, expectedRevision, updatedAt, cancellationToken);

    private ValueTask<PublicationSlotTransitionResult> TransitionAsync(
        string workflowDefinitionId, string slotName, string? publicationId, long expectedRevision,
        DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (publicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        cancellationToken.ThrowIfCancellationRequested();

        var slotId = PublicationSlotIdentity.Create(workflowDefinitionId, slotName);
        var loaded = Load<SlotDocument>(slotId);
        var current = loaded?.Document.Slot ?? new PublicationSlot(slotId, workflowDefinitionId, slotName, null, 0, updatedAt);
        if (current.Revision != expectedRevision)
            return ValueTask.FromResult(Failed(current, "slot_revision_conflict", "The publication slot revision changed."));

        if (publicationId is not null)
        {
            // Uniqueness is enforced by the index; this read only turns the common case into a clear
            // answer instead of a write refusal. It cannot be relied on alone — two activations can both
            // read nothing here — which is why the refusal below is the authority.
            var owner = FindActiveOwner(publicationId);
            if (owner is not null && !StringComparer.Ordinal.Equals(owner.Slot.SlotId, slotId))
                return ValueTask.FromResult(Failed(current, "publication_already_active", "The publication is already active in another slot."));
        }

        var next = current with { ActivePublicationId = publicationId, Revision = current.Revision + 1, UpdatedAt = updatedAt };
        var outcome = Save(
            slotId,
            new SlotDocument(workflowDefinitionId, next),
            loaded?.Entry.Version,
            Projections(workflowDefinitionId, publicationId));
        if (outcome.Status == WriteOutcomeStatus.UniqueViolation)
            return ValueTask.FromResult(Failed(current, "publication_already_active", "The publication is already active in another slot."));
        if (!outcome.Succeeded)
        {
            var winner = Load<SlotDocument>(slotId)?.Document.Slot ?? current;
            return ValueTask.FromResult(Failed(winner, "slot_revision_conflict", "The publication slot revision changed."));
        }

        return ValueTask.FromResult(new PublicationSlotTransitionResult(true, next, current.ActivePublicationId));
    }

    /// <summary>The slot holding <paramref name="publicationId"/>, of which the unique index admits at most one.</summary>
    private SlotDocument? FindActiveOwner(string publicationId)
    {
        var rows = Storage.Query(
            UnitId,
            Storage.Equal(UnitId, PublishingGroundworkStorageManifest.ActivePublicationIdField, publicationId),
            [Storage.Order(UnitId, PublishingGroundworkStorageManifest.ActivePublicationIdField)],
            PublishingGroundworkStorageManifest.SlotByActivePublicationIndex,
            take: 1);
        return rows.Count == 0 ? null : Read<SlotDocument>(rows[0]);
    }

    private static IReadOnlyDictionary<string, object?> Projections(string workflowDefinitionId, string? activePublicationId) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.WorkflowDefinitionIdField] = workflowDefinitionId,
            [PublishingGroundworkStorageManifest.ActivePublicationIdField] = activePublicationId
        };

    private static PublicationSlotTransitionResult Failed(PublicationSlot slot, string code, string message) =>
        new(false, slot, Failure: new PublicationFailure(code, message));

    private sealed record SlotDocument(string WorkflowDefinitionId, PublicationSlot Slot);
}
