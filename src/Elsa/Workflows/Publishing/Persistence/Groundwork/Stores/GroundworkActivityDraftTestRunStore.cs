using System.Globalization;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityDraftTestRunStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore boundedStore)
    : GroundworkPublishingStore(
        store,
        serializer,
        PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind,
        boundedStore),
        IActivityDraftTestRunStore
{
    public async ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        var result = await SaveAsync(receipt.TestRunId, receipt, 0, cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return new(true, receipt);

        var existing = await FindAsync(receipt.TestRunId, cancellationToken)
                       ?? throw new InvalidOperationException("The activity Test Run receipt was created concurrently but could not be read.");
        return new(false, existing);
    }

    public async ValueTask<ActivityDraftTestRunReceipt?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        var receipt = (await LoadAsync<ActivityDraftTestRunReceipt>(testRunId, cancellationToken))?.Document;
        if (receipt is not null)
        {
            ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
            accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        }
        return receipt;
    }

    public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(
        string operationScope,
        string draftId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        FindAsync(ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draftId, idempotencyKey), cancellationToken);

    public async ValueTask<bool> TryUpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        if (receipt.Revision != expectedRevision + 1)
            throw new ArgumentException("The next receipt revision must advance the expected revision exactly once.", nameof(receipt));
        var loaded = await LoadAsync<ActivityDraftTestRunReceipt>(receipt.TestRunId, cancellationToken);
        if (loaded is null || loaded.Value.Document.Revision != expectedRevision)
            return false;
        var result = await SaveAsync(receipt.TestRunId, receipt, loaded.Value.Envelope.Version, cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                    PublishingGroundworkStorageManifest.DeleteExpiredQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    PublishingGroundworkStorageManifest.ReceiptExpiresAtField,
                    asOf.ToString("O", CultureInfo.InvariantCulture)))],
                [new DocumentQueryOrder(PublishingGroundworkStorageManifest.ReceiptExpiresAtField)],
                take: limit),
            cancellationToken);
        var deleted = 0;
        foreach (var envelope in result.Documents)
        {
            var receipt = Serializer.Deserialize<ActivityDraftTestRunReceipt>(envelope);
            accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
            var deletion = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, envelope.Id, envelope.Version),
                cancellationToken);
            if (deletion.Status == DocumentStoreWriteStatus.Deleted)
                deleted++;
        }
        return deleted;
    }
}
