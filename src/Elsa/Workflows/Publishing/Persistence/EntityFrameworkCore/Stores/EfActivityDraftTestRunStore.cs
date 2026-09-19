using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF adapter for activity draft test-run receipts: create-only by test-run id, revision compare-and-swap on
/// update, and bounded, deterministic cleanup of expired receipts within the caller's persistence scope.
/// </summary>
public sealed class EfActivityDraftTestRunStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IActivityDraftTestRunStore
{
    public async ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(receipt);
        if (receipt.Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(receipt), "A new activity Test Run receipt starts at revision 1 or later.");
        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var existing = await FindEntityAsync(scope, receipt.TestRunId, cancellationToken);
        if (existing is not null)
            return new ActivityDraftTestRunCreateResult(false, Authorized(ToModel(existing, scope)));

        var created = ToEntity(receipt, scope);
        context.ActivityDraftTestRuns.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new ActivityDraftTestRunCreateResult(true, receipt);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            var winner = await FindEntityAsync(scope, receipt.TestRunId, cancellationToken)
                         ?? throw new InvalidOperationException("The activity Test Run receipt could not be created, and no concurrent receipt exists.");
            return new ActivityDraftTestRunCreateResult(false, Authorized(ToModel(winner, scope)));
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, created);
        }
    }

    public async ValueTask<ActivityDraftTestRunReceipt?> FindAsync(string testRunId, CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(testRunId, nameof(testRunId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityAsync(scope, testRunId, cancellationToken);
        return row is null ? null : Authorized(ToModel(row, scope));
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
        cancellationToken.ThrowIfCancellationRequested();
        Validate(receipt);
        if (receipt.Revision != expectedRevision + 1)
            throw new ArgumentException("The next receipt revision must advance the expected revision exactly once.", nameof(receipt));

        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityForUpdateAsync(scope, receipt.TestRunId, cancellationToken);
        if (row is null)
            return false;

        try
        {
            var current = Authorized(ToModel(row, scope));
            if (current.Revision != expectedRevision)
                return false;
            EnsureSameIdentity(current, receipt);
            CopyMutable(row, receipt);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another update advanced the revision first; the compare-and-swap lost.
            return false;
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, row);
        }
    }

    public async ValueTask<int> DeleteExpiredAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var tenantHash = EfPublishingStoreSupport.TenantHash(scope);
        var cutoff = asOf.UtcTicks;
        var candidates = await context.ActivityDraftTestRuns.AsNoTracking()
            .Where(row => row.TenantIdHash == tenantHash && row.ReceiptExpiresAtUtcTicks <= cutoff)
            .OrderBy(row => row.ReceiptExpiresAtUtcTicks)
            .ThenBy(row => row.TestRunIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        var deleted = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Decode and authorize before deleting: a malformed or foreign row fails the sweep closed rather
            // than being removed unread.
            _ = Authorized(ToModel(candidate, scope));
            // The revision and expiry are re-checked by the delete itself, so a sweep racing an in-flight
            // receipt update skips the row instead of dropping a newer revision.
            deleted += await context.ActivityDraftTestRuns
                .Where(row => row.Id == candidate.Id &&
                              row.TenantIdHash == tenantHash &&
                              row.Revision == candidate.Revision &&
                              row.ReceiptExpiresAtUtcTicks <= cutoff)
                .ExecuteDeleteAsync(cancellationToken) == 1 ? 1 : 0;
        }

        return deleted;
    }

    private async Task<ActivityDraftTestRunEntity?> FindEntityAsync(string? scope, string testRunId, CancellationToken cancellationToken)
    {
        var candidates = await context.ActivityDraftTestRuns.AsNoTracking()
            .Where(row => row.TenantIdHash == EfPublishingStoreSupport.TenantHash(scope) &&
                          row.TestRunIdHash == EfPublishingStoreSupport.Hash(testRunId))
            .OrderBy(row => row.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return null;

        if (candidates.Length != 1 ||
            !StringComparer.Ordinal.Equals(candidates[0].TenantId, EfPublishingStoreSupport.EncodeNullable(scope)) ||
            !StringComparer.Ordinal.Equals(candidates[0].TestRunId, EfPublishingStoreSupport.Encode(testRunId)))
            throw new InvalidOperationException("An activity Test Run receipt hash candidate did not match its encoded identity residual.");
        return candidates[0];
    }

    private async Task<ActivityDraftTestRunEntity?> FindEntityForUpdateAsync(string? scope, string testRunId, CancellationToken cancellationToken)
    {
        // Attach a fresh snapshot so its database revision is the concurrency original value, rather than an
        // older instance this context may already track.
        var row = await FindEntityAsync(scope, testRunId, cancellationToken);
        if (row is null)
            return null;

        var tracked = context.ChangeTracker.Entries<ActivityDraftTestRunEntity>()
            .FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.Entity.Id, row.Id));
        tracked?.State = EntityState.Detached;
        context.ActivityDraftTestRuns.Attach(row);
        return row;
    }

    private ActivityDraftTestRunReceipt Authorized(ActivityDraftTestRunReceipt receipt)
    {
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        return receipt;
    }

    private static ActivityDraftTestRunEntity ToEntity(ActivityDraftTestRunReceipt receipt, string? scope)
    {
        var row = new ActivityDraftTestRunEntity
        {
            Id = EfPublishingStoreSupport.PhysicalId(scope, receipt.TestRunId),
            TestRunId = EfPublishingStoreSupport.Encode(receipt.TestRunId),
            TestRunIdHash = EfPublishingStoreSupport.Hash(receipt.TestRunId),
            TestRunIdOrderKey = EfPublishingStoreSupport.OrderKey(receipt.TestRunId),
            SchemaVersion = PublishingLedgerEfModule.ContentSchemaVersion,
            TenantId = EfPublishingStoreSupport.EncodeNullable(scope),
            TenantIdHash = EfPublishingStoreSupport.TenantHash(scope)
        };
        CopyMutable(row, receipt);
        return row;
    }

    private static void CopyMutable(ActivityDraftTestRunEntity row, ActivityDraftTestRunReceipt receipt)
    {
        var expiry = EfPublishingStoreSupport.DateTimeOffsetParts(receipt.ReceiptExpiresAt);
        row.ReceiptExpiresAtUtcTicks = expiry.UtcTicks;
        row.ReceiptExpiresAtOffsetMinutes = expiry.OffsetMinutes;
        row.Status = receipt.Status.ToString();
        row.Content = PublishingEfJson.Serialize(receipt);
        row.Revision = receipt.Revision;
    }

    private static ActivityDraftTestRunReceipt ToModel(ActivityDraftTestRunEntity row, string? scope)
    {
        var testRunId = EfPublishingStoreSupport.DecodeIdentity(row.TestRunId, nameof(row.TestRunId));
        if (!StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(scope, testRunId)) ||
            !StringComparer.Ordinal.Equals(row.TenantId, EfPublishingStoreSupport.EncodeNullable(scope)) ||
            !StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(scope)))
            throw new InvalidOperationException("The persisted activity Test Run receipt scope projection is corrupt.");
        EfPublishingStoreSupport.EnsureProjection(testRunId, row.TestRunIdHash, row.TestRunIdOrderKey, nameof(row.TestRunId));
        if (!StringComparer.Ordinal.Equals(row.SchemaVersion, PublishingLedgerEfModule.ContentSchemaVersion))
            throw new InvalidOperationException($"Malformed persisted publication state: activity Test Run receipt schema version '{row.SchemaVersion}' is not supported.");

        var receipt = PublishingEfJson.Deserialize<ActivityDraftTestRunReceipt>(row.Content, "activity Test Run receipt");
        var expiry = EfPublishingStoreSupport.DateTimeOffset(row.ReceiptExpiresAtUtcTicks, row.ReceiptExpiresAtOffsetMinutes);
        if (!StringComparer.Ordinal.Equals(receipt.TestRunId, testRunId) ||
            receipt.Revision != row.Revision ||
            receipt.Revision < 1 ||
            receipt.ReceiptExpiresAt != expiry ||
            receipt.ReceiptExpiresAt.Offset != expiry.Offset ||
            !Enum.IsDefined(receipt.Status) ||
            !StringComparer.Ordinal.Equals(row.Status, receipt.Status.ToString()) ||
            !Enum.IsDefined(receipt.CancellationStatus))
            throw new InvalidOperationException("The persisted activity Test Run receipt projections do not match its material.");
        return receipt;
    }

    private static void EnsureSameIdentity(ActivityDraftTestRunReceipt current, ActivityDraftTestRunReceipt next)
    {
        if (current.TestRunId != next.TestRunId || current.OperationScope != next.OperationScope ||
            current.IdempotencyKeyHash != next.IdempotencyKeyHash || current.DraftId != next.DraftId ||
            current.DraftRevision != next.DraftRevision || current.DefinitionId != next.DefinitionId ||
            current.TenantId != next.TenantId || current.ResourceTenantId != next.ResourceTenantId ||
            current.RequestFingerprint != next.RequestFingerprint || current.WorkflowExecutionId != next.WorkflowExecutionId ||
            current.RequestedAt != next.RequestedAt)
            throw new InvalidOperationException("An activity Test Run receipt update cannot change the receipt's request identity.");
    }

    private void Validate(ActivityDraftTestRunReceipt receipt)
    {
        EfPublishingStoreSupport.EnsureIdentity(receipt.TestRunId, nameof(receipt.TestRunId));
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        accessContextAccessor.Current.EnsureTenantScope(receipt.TenantId);
        if (!Enum.IsDefined(receipt.Status) || !Enum.IsDefined(receipt.CancellationStatus))
            throw new ArgumentException("The activity Test Run receipt contains an invalid enum value.", nameof(receipt));
    }
}
