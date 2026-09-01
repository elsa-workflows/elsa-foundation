using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityDraftTestRunStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string? targetName = null)
    : GroundworkPublishingStore(
        sessions,
        accessContextAccessor,
        serializer,
        PublishingGroundworkStorageManifest.ActivityDraftTestRunDocumentKind,
        targetName),
        IActivityDraftTestRunStore
{
    public ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        AccessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);

        if (SaveSucceeded(receipt.TestRunId, receipt, null, Projections(receipt)))
            return ValueTask.FromResult(new ActivityDraftTestRunCreateResult(true, receipt));

        var existing = Load<ActivityDraftTestRunReceipt>(receipt.TestRunId)?.Document
                       ?? throw new InvalidOperationException("The activity Test Run receipt was created concurrently but could not be read.");
        ActivityDraftTestRunIdentity.EnsureOperationScope(existing);
        AccessContextAccessor.Current.EnsureTenantScope(existing.TenantId);
        return ValueTask.FromResult(new ActivityDraftTestRunCreateResult(false, existing));
    }

    public ValueTask<ActivityDraftTestRunReceipt?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        cancellationToken.ThrowIfCancellationRequested();
        var receipt = Load<ActivityDraftTestRunReceipt>(testRunId)?.Document;
        if (receipt is not null)
        {
            ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
            AccessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        }
        return ValueTask.FromResult(receipt);
    }

    public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(
        string operationScope,
        string draftId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        FindAsync(ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draftId, idempotencyKey), cancellationToken);

    public ValueTask<bool> TryUpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        AccessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        if (receipt.Revision != expectedRevision + 1)
            throw new ArgumentException("The next receipt revision must advance the expected revision exactly once.", nameof(receipt));

        var loaded = Load<ActivityDraftTestRunReceipt>(receipt.TestRunId);
        if (loaded is null || loaded.Value.Document.Revision != expectedRevision)
            return ValueTask.FromResult(false);
        return ValueTask.FromResult(SaveSucceeded(receipt.TestRunId, receipt, loaded.Value.Entry.Version, Projections(receipt)));
    }

    public ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();

        var expired = Storage.Query(
            UnitId,
            Storage.AtOrBefore(UnitId, PublishingGroundworkStorageManifest.ReceiptExpiresAtField, asOf),
            [
                Storage.Order(UnitId, PublishingGroundworkStorageManifest.ReceiptExpiresAtField),
                Storage.Order(UnitId, PublishingGroundworkStorageManifest.IdField)
            ],
            PublishingGroundworkStorageManifest.DraftTestRunByExpiryIndex,
            limit,
            cancellationToken);

        var deleted = 0;
        foreach (var row in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var testRunId = Text(row.Values.Values, PublishingGroundworkStorageManifest.IdField);
            if (testRunId is null)
                continue;
            // Re-read to take the row under the version the delete asserts: a sweep races an in-flight
            // receipt update, and losing that race must skip the row rather than drop a newer revision.
            var loaded = Load<ActivityDraftTestRunReceipt>(testRunId);
            if (loaded is null)
                continue;
            AccessContextAccessor.Current.EnsureTenantScope(loaded.Value.Document.TenantId);
            var version = loaded.Value.Entry.Version;
            var outcome = Storage.Delete(
                UnitId,
                testRunId,
                version is null ? WriteOptions.Unconditional : WriteOptions.IfVersion(version.Value));
            if (outcome.Succeeded)
                deleted++;
        }

        return ValueTask.FromResult(deleted);
    }

    private static IReadOnlyDictionary<string, object?> Projections(ActivityDraftTestRunReceipt receipt) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.ReceiptExpiresAtField] = receipt.ReceiptExpiresAt
        };
}
