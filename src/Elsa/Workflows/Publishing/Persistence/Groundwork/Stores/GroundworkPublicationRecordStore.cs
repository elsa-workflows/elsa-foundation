using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationRecordStore(IDocumentStore store, PublishingGroundworkDocumentSerializer serializer)
    : GroundworkPublishingStore(store, serializer, PublishingGroundworkStorageManifest.PublicationRecordDocumentKind), IPublicationRecordStore
{
    public async ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var loaded = await LoadAsync<RecordDocument>(publication.PublicationId, cancellationToken);
        if (loaded is not null)
        {
            if (loaded.Value.Document.Publication != publication)
                throw new InvalidOperationException($"Publication '{publication.PublicationId}' already exists.");
            return;
        }
        var result = await SaveAsync(publication.PublicationId, new RecordDocument(publication.SlotId, publication), 0, cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException($"Publication '{publication.PublicationId}' was created concurrently.");
    }

    public async ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) =>
        (await LoadAsync<RecordDocument>(publicationId, cancellationToken))?.Document.Publication;

    public async ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default)
    {
        var docs = await QueryAsync<RecordDocument>(PublishingGroundworkStorageManifest.BySlotIndex, slotId, cancellationToken);
        return docs.Select(x => x.Publication).OrderBy(x => x.CreatedAt).ThenBy(x => x.PublicationId, StringComparer.Ordinal).ToArray();
    }

    public async ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync<RecordDocument>(publication.PublicationId, cancellationToken);
        if (loaded is null || loaded.Value.Document.Publication.Status != expectedStatus)
            return false;
        EnsureSameIdentity(loaded.Value.Document.Publication, publication);
        var result = await SaveAsync(publication.PublicationId, new RecordDocument(publication.SlotId, publication), loaded.Value.Envelope.Version, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    private static void EnsureSameIdentity(PublicationRecord current, PublicationRecord next)
    {
        if (current.PublicationId != next.PublicationId || current.SlotId != next.SlotId || current.SlotName != next.SlotName ||
            current.WorkflowDefinitionId != next.WorkflowDefinitionId || current.WorkflowDefinitionVersionId != next.WorkflowDefinitionVersionId ||
            current.ArtifactId != next.ArtifactId || current.ExpectedSlotRevision != next.ExpectedSlotRevision || current.CreatedAt != next.CreatedAt)
            throw new InvalidOperationException("A publication lifecycle transition cannot change immutable publication identity.");
    }

    private sealed record RecordDocument(string SlotId, PublicationRecord Publication);
}
