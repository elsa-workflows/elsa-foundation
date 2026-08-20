using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationRecordStore(
    GroundworkPublishingStorage storage,
    PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(storage, serializer, PublishingGroundworkStorageManifest.PublicationRecordDocumentKind),
        IPublicationRecordStore
{
    public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<RecordDocument>(publication.PublicationId);
        if (loaded is not null)
        {
            if (loaded.Value.Document.Publication != publication)
                throw new InvalidOperationException($"Publication '{publication.PublicationId}' already exists.");
            return ValueTask.CompletedTask;
        }

        if (!SaveSucceeded(publication.PublicationId, new RecordDocument(publication.SlotId, publication), null, Projections(publication.SlotId)))
            throw new InvalidOperationException($"Publication '{publication.PublicationId}' was created concurrently.");
        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load<RecordDocument>(publicationId)?.Document.Publication);
    }

    public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var docs = QueryBy<RecordDocument>(
            PublishingGroundworkStorageManifest.SlotIdField,
            slotId,
            PublishingGroundworkStorageManifest.RecordBySlotIndex);
        return ValueTask.FromResult<IReadOnlyCollection<PublicationRecord>>(
            docs.Select(x => x.Publication)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.PublicationId, StringComparer.Ordinal)
                .ToArray());
    }

    public ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<RecordDocument>(publication.PublicationId);
        if (loaded is null || loaded.Value.Document.Publication.Status != expectedStatus)
            return ValueTask.FromResult(false);
        EnsureSameIdentity(loaded.Value.Document.Publication, publication);
        return ValueTask.FromResult(SaveSucceeded(
            publication.PublicationId,
            new RecordDocument(publication.SlotId, publication),
            loaded.Value.Entry.Version,
            Projections(publication.SlotId)));
    }

    private static IReadOnlyDictionary<string, object?> Projections(string slotId) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.SlotIdField] = slotId
        };

    private static void EnsureSameIdentity(PublicationRecord current, PublicationRecord next)
    {
        if (current.PublicationId != next.PublicationId || current.SlotId != next.SlotId || current.SlotName != next.SlotName ||
            current.WorkflowDefinitionId != next.WorkflowDefinitionId || current.WorkflowDefinitionVersionId != next.WorkflowDefinitionVersionId ||
            current.ArtifactId != next.ArtifactId || current.ExpectedSlotRevision != next.ExpectedSlotRevision || current.CreatedAt != next.CreatedAt)
            throw new InvalidOperationException("A publication lifecycle transition cannot change immutable publication identity.");
    }

    private sealed record RecordDocument(string SlotId, PublicationRecord Publication);
}
