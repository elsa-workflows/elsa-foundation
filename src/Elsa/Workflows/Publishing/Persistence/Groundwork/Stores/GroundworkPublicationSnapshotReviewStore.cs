using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

/// <summary>
/// Cross-replica review authority backed by Groundwork CAS. Consumption deletes the exact loaded version, so
/// only one publisher can win even when multiple server replicas validate the same token concurrently.
/// </summary>
public sealed class GroundworkPublicationSnapshotReviewStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string? targetName = null)
    : GroundworkPublishingStore(
        sessions,
        accessContextAccessor,
        serializer,
        PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind,
        targetName),
        IPublicationSnapshotReviewStore
{
    public ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        cancellationToken.ThrowIfCancellationRequested();
        AccessContextAccessor.Current.EnsureTenantScope(review.TenantId);
        return ValueTask.FromResult(SaveSucceeded(review.PreflightToken, review, null, Projections(review)));
    }

    public ValueTask<PublicationSnapshotReview?> FindAsync(
        string preflightToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var review = Load<PublicationSnapshotReview>(preflightToken)?.Document;
        if (review is not null)
            AccessContextAccessor.Current.EnsureTenantScope(review.TenantId);
        return ValueTask.FromResult(review);
    }

    public ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load<PublicationSnapshotReview>(preflightToken);
        if (loaded is null)
            return ValueTask.FromResult(false);
        AccessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.TenantId);
        return ValueTask.FromResult(Delete(preflightToken, loaded.Value.Entry.Version));
    }

    public ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        var expired = Storage.Query(
            UnitId,
            Storage.AtOrBefore(UnitId, PublishingGroundworkStorageManifest.ExpiresAtField, expiresAtOrBefore),
            [
                Storage.Order(UnitId, PublishingGroundworkStorageManifest.ExpiresAtField),
                Storage.Order(UnitId, PublishingGroundworkStorageManifest.IdField)
            ],
            PublishingGroundworkStorageManifest.SnapshotReviewByExpiryIndex,
            maxCount,
            cancellationToken);

        var deleted = 0;
        foreach (var row in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = Text(row.Values.Values, PublishingGroundworkStorageManifest.IdField);
            if (token is null)
                continue;
            // Re-read to take the row under the version the delete will assert. A sweep races ordinary
            // consumption, and losing that race must skip the row rather than delete a newer one.
            var loaded = Load<PublicationSnapshotReview>(token);
            if (loaded is null)
                continue;
            AccessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.TenantId);
            if (Delete(token, loaded.Value.Entry.Version))
                deleted++;
        }

        return ValueTask.FromResult(deleted);
    }

    private bool Delete(string id, long? version) =>
        Storage.Delete(
            UnitId,
            id,
            version is null ? WriteOptions.Unconditional : WriteOptions.IfVersion(version.Value)).Succeeded;

    private static IReadOnlyDictionary<string, object?> Projections(PublicationSnapshotReview review) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.ExpiresAtField] = review.ExpiresAt
        };
}
