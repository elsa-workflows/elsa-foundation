using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkPublicationProjectionIntentStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string? targetName = null)
    : GroundworkPublishingStore(
        sessions,
        accessContextAccessor,
        serializer,
        PublishingGroundworkStorageManifest.ProjectionIntentDocumentKind,
        targetName),
        IPublicationProjectionIntentStore
{
    public ValueTask SaveAsync(PublicationProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<IntentDocument>(intent.IntentId);
        if (loaded is not null)
        {
            if (loaded.Value.Document.Intent != intent)
                throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' already exists.");
            return ValueTask.CompletedTask;
        }

        if (!SaveSucceeded(intent.IntentId, new IntentDocument(intent.PublicationId, intent), null, Projections(intent.PublicationId)))
            throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' was created concurrently.");
        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicationProjectionIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load<IntentDocument>(intentId)?.Document.Intent);
    }

    public ValueTask<IReadOnlyCollection<PublicationProjectionIntent>> ListByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var docs = QueryBy<IntentDocument>(
            PublishingGroundworkStorageManifest.PublicationIdField,
            publicationId,
            PublishingGroundworkStorageManifest.IntentByPublicationIndex);
        return ValueTask.FromResult<IReadOnlyCollection<PublicationProjectionIntent>>(
            docs.Select(x => x.Intent).OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray());
    }

    public ValueTask<PublicationProjectionIntentTransitionResult> TryTransitionAsync(
        PublicationProjectionIntent intent,
        PublicationProjectionIntentStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<IntentDocument>(intent.IntentId);
        if (loaded is null || loaded.Value.Document.Intent.Status != expectedStatus)
            return ValueTask.FromResult(new PublicationProjectionIntentTransitionResult(false, loaded?.Document.Intent ?? intent));

        var current = loaded.Value.Document.Intent;
        if (current.PublicationId != intent.PublicationId || current.ProjectionKind != intent.ProjectionKind || current.Operation != intent.Operation)
            throw new InvalidOperationException("A projection-intent transition cannot change immutable delivery identity.");

        if (SaveSucceeded(intent.IntentId, new IntentDocument(intent.PublicationId, intent), loaded.Value.Entry.Version, Projections(intent.PublicationId)))
            return ValueTask.FromResult(new PublicationProjectionIntentTransitionResult(true, intent));

        var winner = Load<IntentDocument>(intent.IntentId)?.Document.Intent ?? intent;
        return ValueTask.FromResult(new PublicationProjectionIntentTransitionResult(false, winner));
    }

    private static IReadOnlyDictionary<string, object?> Projections(string publicationId) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.PublicationIdField] = publicationId
        };

    private sealed record IntentDocument(string PublicationId, PublicationProjectionIntent Intent);
}
