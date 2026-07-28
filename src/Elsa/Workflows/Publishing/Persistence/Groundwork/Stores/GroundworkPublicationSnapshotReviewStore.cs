using System.Globalization;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

/// <summary>
/// Cross-replica review authority backed by Groundwork CAS. Consumption deletes the exact loaded version, so
/// only one publisher can win even when multiple server replicas validate the same token concurrently.
/// </summary>
public sealed class GroundworkPublicationSnapshotReviewStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkPublishingStore(
        store,
        serializer,
        PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind,
        boundedStore),
        IPublicationSnapshotReviewStore
{
    public async ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        accessContextAccessor.Current.EnsureTenantScope(review.TenantId);
        var result = await SaveAsync(review.PreflightToken, review, 0, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask<PublicationSnapshotReview?> FindAsync(
        string preflightToken,
        CancellationToken cancellationToken = default)
    {
        var review = (await LoadAsync<PublicationSnapshotReview>(preflightToken, cancellationToken))?.Document;
        if (review is not null)
            accessContextAccessor.Current.EnsureTenantScope(review.TenantId);
        return review;
    }

    public async ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync<PublicationSnapshotReview>(preflightToken, cancellationToken);
        if (loaded is null)
            return false;
        accessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.TenantId);
        var result = await Store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, preflightToken, loaded.Value.Envelope.Version),
            cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                PublishingGroundworkStorageManifest.DeleteExpiredQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    PublishingGroundworkStorageManifest.ExpiresAtField,
                    expiresAtOrBefore.ToString("O", CultureInfo.InvariantCulture)))],
                [new DocumentQueryOrder(PublishingGroundworkStorageManifest.ExpiresAtField)],
                take: maxCount),
            cancellationToken);
        var deleted = 0;
        foreach (var envelope in result.Documents)
        {
            var review = Serializer.Deserialize<PublicationSnapshotReview>(envelope);
            accessContextAccessor.Current.EnsureTenantScope(review.TenantId);
            var deletion = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, envelope.Id, envelope.Version),
                cancellationToken);
            if (deletion.Status == DocumentStoreWriteStatus.Deleted)
                deleted++;
        }
        return deleted;
    }
}
