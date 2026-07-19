using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationProjectionIntentStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer,
    IBoundedDocumentStore queries)
    : GroundworkPublishingStore(
        store,
        serializer,
        PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind,
        queries ?? throw new ArgumentNullException(nameof(queries))), IPublicationProjectionIntentStore
{
    public async ValueTask SaveAsync(PublicationProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var loaded = await LoadAsync<IntentDocument>(intent.IntentId, cancellationToken);
        if (loaded is not null)
        {
            if (loaded.Value.Document.Intent != intent)
                throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' already exists.");
            return;
        }
        var result = await SaveAsync(intent.IntentId, new IntentDocument(intent.PublicationId, intent), 0, cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' was created concurrently.");
    }

    public async ValueTask<PublicationProjectionIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default) =>
        (await LoadAsync<IntentDocument>(intentId, cancellationToken))?.Document.Intent;

    public async ValueTask<IReadOnlyCollection<PublicationProjectionIntent>> ListByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        var docs = await QueryAsync<IntentDocument>(
            PublishingGroundworkStorageManifest.ListByPublicationQuery,
            PublishingGroundworkStorageManifest.PublicationIdField,
            publicationId,
            cancellationToken);
        return docs.Select(x => x.Intent).OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray();
    }

    public async ValueTask<PublicationProjectionIntentTransitionResult> TryTransitionAsync(
        PublicationProjectionIntent intent,
        PublicationProjectionIntentStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync<IntentDocument>(intent.IntentId, cancellationToken);
        if (loaded is null || loaded.Value.Document.Intent.Status != expectedStatus)
            return new PublicationProjectionIntentTransitionResult(false, loaded?.Document.Intent ?? intent);
        var current = loaded.Value.Document.Intent;
        if (current.PublicationId != intent.PublicationId || current.ProjectionKind != intent.ProjectionKind || current.Operation != intent.Operation)
            throw new InvalidOperationException("A projection-intent transition cannot change immutable delivery identity.");
        var result = await SaveAsync(intent.IntentId, new IntentDocument(intent.PublicationId, intent), loaded.Value.Envelope.Version, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved
            ? new PublicationProjectionIntentTransitionResult(true, intent)
            : new PublicationProjectionIntentTransitionResult(false, (await FindAsync(intent.IntentId, cancellationToken)) ?? intent);
    }

    private sealed record IntentDocument(string PublicationId, PublicationProjectionIntent Intent);
}
