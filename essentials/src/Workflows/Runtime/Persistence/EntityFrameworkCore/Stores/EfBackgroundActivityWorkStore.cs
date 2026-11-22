using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF worker store and relational projections for the durable background activity ledger.</summary>
public sealed class EfBackgroundActivityWorkStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IBackgroundActivityWorkStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<BackgroundActivityWorkItem?> FindAsync(
        string workflowExecutionId,
        string workId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(workflowExecutionId, nameof(workflowExecutionId));
        ValidateKey(workId, nameof(workId));
        var scope = Scope();
        var row = await LoadAsync(scope, workflowExecutionId, workId, tracking: false, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, workId);
    }

    public async ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout));
        var scope = Scope();
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var nowTicks = now.UtcTicks;
        var candidates = await context.BackgroundActivityWorks.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.ClaimableIsEligible && row.ClaimableAtUtcTicks <= nowTicks)
            .OrderBy(row => row.ClaimableAtUtcTicks)
            .ThenBy(row => row.RecordedAtUtcTicks)
            .ThenBy(row => row.WorkflowExecutionIdHash)
            .ThenBy(row => row.WorkIdHash)
            .Take(64)
            .ToArrayAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ReadChecked(candidate, scope, candidate.WorkflowExecutionId, candidate.WorkId);
            var row = await LoadAsync(scope, candidate.WorkflowExecutionId, candidate.WorkId, tracking: true, cancellationToken);
            if (row is null)
                continue;

            var current = ReadChecked(row, scope, candidate.WorkflowExecutionId, candidate.WorkId);
            var updated = BackgroundActivityWorkLifecycle.TryClaim(current, ownerId, now, visibilityTimeout, out var claim);
            if (updated is null)
            {
                Detach(row);
                continue;
            }

            Copy(row, updated, scope);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return claim;
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(row);
                // A racing worker won this item. A later poll can claim the next currently eligible row.
            }
            catch
            {
                Detach(row);
                throw;
            }
        }

        return null;
    }

    public async ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(
        BackgroundActivityWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var scope = Scope();
        var row = await LoadAsync(scope, claim.WorkflowExecutionId, claim.WorkId, tracking: true, cancellationToken);
        if (row is null)
            return null;
        var current = ReadChecked(row, scope, claim.WorkflowExecutionId, claim.WorkId);
        var updated = BackgroundActivityWorkLifecycle.TryRenewClaim(current, claim, now, visibilityTimeout, out var renewed);
        if (updated is null)
        {
            Detach(row);
            return null;
        }

        Copy(row, updated, scope);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return renewed;
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(row);
            return null;
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<BackgroundActivityWorkItem> RecordResultAsync(
        BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        var scope = Scope();
        var row = await LoadAsync(scope, claim.WorkflowExecutionId, claim.WorkId, tracking: true, cancellationToken)
                  ?? throw new BackgroundActivityWorkConflictException(claim.WorkflowExecutionId, claim.WorkId, "the work item was not found in this persistence scope");
        var current = ReadChecked(row, scope, claim.WorkflowExecutionId, claim.WorkId);
        var updated = BackgroundActivityWorkLifecycle.RecordResult(current, claim, result, now);
        if (ReferenceEquals(current, updated))
            return current;

        Copy(row, updated, scope);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return updated;
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(row);
            var latest = await LoadAsync(scope, claim.WorkflowExecutionId, claim.WorkId, tracking: false, cancellationToken);
            if (latest is not null)
                return BackgroundActivityWorkLifecycle.RecordResult(
                    ReadChecked(latest, scope, claim.WorkflowExecutionId, claim.WorkId), claim, result, now);
            throw new BackgroundActivityWorkConflictException(claim.WorkflowExecutionId, claim.WorkId, "the work item changed concurrently and is no longer visible");
        }
        catch
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(
        int limit,
        string? workflowExecutionId = null,
        CancellationToken cancellationToken = default)
    {
        if (limit is <= 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "Result-ready listing limit must be between 1 and 500.");
        if (workflowExecutionId is not null)
            ValidateKey(workflowExecutionId, nameof(workflowExecutionId));
        var scope = Scope();
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var ready = (int)BackgroundActivityWorkStatus.ResultReady;
        var rows = await context.BackgroundActivityWorks.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.Status == ready &&
                          (workflowExecutionId == null || row.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId)))
            .OrderBy(row => row.UpdatedAtUtcTicks)
            .ThenBy(row => row.WorkflowExecutionIdHash)
            .ThenBy(row => row.WorkIdHash)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => ReadChecked(row, scope, row.WorkflowExecutionId, row.WorkId)).ToArray();
    }

    internal static string RowId(string scope, string workflowExecutionId, string workId) =>
        EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, workId);

    internal static BackgroundActivityWorkEntity ToEntity(BackgroundActivityWorkItem item, string scope)
    {
        ValidateItem(item);
        var request = item.Request;
        ValidateKey(request.WorkflowExecutionId, nameof(request.WorkflowExecutionId));
        ValidateKey(request.ActivityExecutionId, nameof(request.ActivityExecutionId));
        ValidateKey(request.WorkId, nameof(request.WorkId));
        var projection = Projection(item);
        return new BackgroundActivityWorkEntity
        {
            Id = RowId(scope, request.WorkflowExecutionId, request.WorkId),
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
            ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            WorkflowExecutionId = request.WorkflowExecutionId,
            WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkflowExecutionId),
            ActivityExecutionId = request.ActivityExecutionId,
            ActivityExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(request.ActivityExecutionId),
            WorkId = request.WorkId,
            WorkIdHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkId),
            Status = projection.Status,
            RecordedAtUtcTicks = projection.RecordedAtUtcTicks,
            UpdatedAtUtcTicks = projection.UpdatedAtUtcTicks,
            ClaimableAtUtcTicks = projection.ClaimableAtUtcTicks,
            ClaimableIsEligible = projection.ClaimableIsEligible,
            ContentJson = JsonSerializer.Serialize(item, JsonOptions),
            SchemaVersion = RuntimeBackgroundActivityWorkEfModule.SchemaVersion,
            Revision = item.Revision
        };
    }

    internal static void Copy(BackgroundActivityWorkEntity row, BackgroundActivityWorkItem item, string scope)
    {
        ValidateItem(item);
        var request = item.Request;
        EfRuntimeOperationalStoreSupport.EnsureScope(EfRuntimeOperationalStoreSupport.Decode(row.ScopeKey), scope);
        if (!StringComparer.Ordinal.Equals(row.Id, RowId(scope, request.WorkflowExecutionId, request.WorkId)))
            throw new InvalidDataException("The background activity work row ID does not match its request identity.");
        row.WorkflowExecutionId = request.WorkflowExecutionId;
        row.WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkflowExecutionId);
        row.ActivityExecutionId = request.ActivityExecutionId;
        row.ActivityExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(request.ActivityExecutionId);
        row.WorkId = request.WorkId;
        row.WorkIdHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkId);
        row.Status = (int)item.Status;
        var projection = Projection(item);
        row.RecordedAtUtcTicks = projection.RecordedAtUtcTicks;
        row.UpdatedAtUtcTicks = projection.UpdatedAtUtcTicks;
        row.ClaimableAtUtcTicks = projection.ClaimableAtUtcTicks;
        row.ClaimableIsEligible = projection.ClaimableIsEligible;
        row.ContentJson = JsonSerializer.Serialize(item, JsonOptions);
        row.SchemaVersion = RuntimeBackgroundActivityWorkEfModule.SchemaVersion;
        row.Revision = item.Revision;
    }

    internal static BackgroundActivityWorkItem ReadChecked(
        BackgroundActivityWorkEntity row,
        string scope,
        string workflowExecutionId,
        string workId)
    {
        EfRuntimeOperationalStoreSupport.EnsureScope(EfRuntimeOperationalStoreSupport.Decode(row.ScopeKey), scope);
        ValidateKey(workflowExecutionId, nameof(workflowExecutionId));
        ValidateKey(workId, nameof(workId));
        if (!StringComparer.Ordinal.Equals(row.ScopeKeyHash, EfRuntimeOperationalStoreSupport.Hash(scope)) ||
            !StringComparer.Ordinal.Equals(row.Id, RowId(scope, workflowExecutionId, workId)) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(row.WorkflowExecutionIdHash, EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId)) ||
            !StringComparer.Ordinal.Equals(row.WorkId, workId) ||
            !StringComparer.Ordinal.Equals(row.WorkIdHash, EfRuntimeOperationalStoreSupport.Hash(workId)) ||
            !StringComparer.Ordinal.Equals(row.SchemaVersion, RuntimeBackgroundActivityWorkEfModule.SchemaVersion))
            throw new InvalidDataException("The persisted background activity work identity or schema projection is inconsistent.");

        var item = JsonSerializer.Deserialize<BackgroundActivityWorkItem>(row.ContentJson, JsonOptions)
                   ?? throw new InvalidDataException("The persisted background activity work content is empty.");
        ValidateItem(item);
        var request = item.Request;
        if (!StringComparer.Ordinal.Equals(request.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(request.WorkId, workId) ||
            !StringComparer.Ordinal.Equals(row.ActivityExecutionId, request.ActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(row.ActivityExecutionIdHash, EfRuntimeOperationalStoreSupport.Hash(request.ActivityExecutionId)) ||
            row.Status != (int)item.Status || row.Revision != item.Revision)
            throw new InvalidDataException("The persisted background activity work content does not match its relational projections.");

        var projection = Projection(item);
        if (row.RecordedAtUtcTicks != projection.RecordedAtUtcTicks ||
            row.UpdatedAtUtcTicks != projection.UpdatedAtUtcTicks ||
            row.ClaimableAtUtcTicks != projection.ClaimableAtUtcTicks ||
            row.ClaimableIsEligible != projection.ClaimableIsEligible)
            throw new InvalidDataException("The persisted background activity work lifecycle projections are inconsistent.");
        return item;
    }

    private async ValueTask<BackgroundActivityWorkEntity?> LoadAsync(
        string scope,
        string workflowExecutionId,
        string workId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var id = RowId(scope, workflowExecutionId, workId);
        if (tracking)
        {
            foreach (var entry in context.ChangeTracker.Entries<BackgroundActivityWorkEntity>()
                         .Where(entry => entry.Entity.Id == id && entry.State == EntityState.Unchanged).ToArray())
                entry.State = EntityState.Detached;
        }
        var query = tracking ? context.BackgroundActivityWorks : context.BackgroundActivityWorks.AsNoTracking();
        return await query.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    private string Scope() => EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);

    private static void ValidateKey(string value, string parameterName)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(value, parameterName);
    }

    private static void ValidateItem(BackgroundActivityWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Request);
        if (!Enum.IsDefined(item.Status) || item.ClaimFence < 0 || item.Revision < 0)
            throw new InvalidDataException("The background activity work lifecycle state is invalid.");
        var hasAnyReceiptField = item.Result is not null || item.ReceiptDigest is not null || item.ResultClaimOwnerId is not null ||
                                 item.ResultClaimFence is not null || item.ResultClaimedAt is not null || item.ResultClaimVisibleUntil is not null ||
                                 item.ResultClaimRevision is not null;
        var hasCompleteReceipt = item.Result is not null && !string.IsNullOrWhiteSpace(item.ReceiptDigest) &&
                                 !string.IsNullOrWhiteSpace(item.ResultClaimOwnerId) && item.ResultClaimFence is not null &&
                                 item.ResultClaimedAt is not null && item.ResultClaimVisibleUntil is not null && item.ResultClaimRevision is not null;
        if (item.Status is BackgroundActivityWorkStatus.ResultReady or BackgroundActivityWorkStatus.Accepted && !hasCompleteReceipt ||
            hasAnyReceiptField && !hasCompleteReceipt)
            throw new InvalidDataException("A background activity item must carry either no receipt or a complete immutable receipt and recording claim.");
        if (hasCompleteReceipt && !StringComparer.Ordinal.Equals(item.ReceiptDigest, BackgroundActivityWorkReceipt.ComputeDigest(item.Request, item.Result!)))
            throw new InvalidDataException("The background activity receipt digest does not match its canonical request and result.");
        if (hasCompleteReceipt &&
            (item.ResultClaimFence is not { } resultFence || resultFence <= 0 || resultFence > item.ClaimFence ||
             item.ResultClaimRevision is not { } resultRevision || resultRevision <= 0 || resultRevision >= item.Revision ||
             item.ResultClaimedAt >= item.ResultClaimVisibleUntil))
            throw new InvalidDataException("The recorded background activity claim is invalid.");

        var hasActiveClaim = item.ClaimOwnerId is not null || item.ClaimedAt is not null || item.VisibleUntil is not null;
        if (item.Status == BackgroundActivityWorkStatus.Claimed)
        {
            if (string.IsNullOrWhiteSpace(item.ClaimOwnerId) || item.ClaimedAt is null || item.VisibleUntil is null ||
                item.ClaimedAt >= item.VisibleUntil || hasAnyReceiptField || item.AcceptedAt is not null || item.CancelledAt is not null)
                throw new InvalidDataException("A claimed background activity item must carry only its current valid lease.");
        }
        else if (hasActiveClaim)
            throw new InvalidDataException("Only a claimed background activity item can carry an active lease.");

        if (item.Status != BackgroundActivityWorkStatus.Accepted && item.AcceptedAt is not null)
            throw new InvalidDataException("Only an accepted background activity item can carry an acceptance instant.");
        if (item.Status != BackgroundActivityWorkStatus.Cancelled && item.CancelledAt is not null)
            throw new InvalidDataException("Only a cancelled background activity item can carry a tombstone instant.");
        if (item.Status == BackgroundActivityWorkStatus.Accepted && item.AcceptedAt is null)
            throw new InvalidDataException("An accepted background activity item must carry its acceptance instant.");
        if (item.Status == BackgroundActivityWorkStatus.Cancelled && item.CancelledAt is null)
            throw new InvalidDataException("A cancelled background activity item must carry its tombstone instant.");
    }

    private static (int Status, long RecordedAtUtcTicks, long UpdatedAtUtcTicks, long ClaimableAtUtcTicks, bool ClaimableIsEligible) Projection(
        BackgroundActivityWorkItem item)
    {
        var claimable = item.Status switch
        {
            BackgroundActivityWorkStatus.Pending => item.RecordedAt.UtcTicks,
            BackgroundActivityWorkStatus.Claimed => item.VisibleUntil?.UtcTicks ?? throw new InvalidDataException("A claimed background activity item has no visibility deadline."),
            _ => 0
        };
        var eligible = item.Status is BackgroundActivityWorkStatus.Pending or BackgroundActivityWorkStatus.Claimed;
        return ((int)item.Status, item.RecordedAt.UtcTicks, item.UpdatedAt.UtcTicks, claimable, eligible);
    }

    private void Detach(BackgroundActivityWorkEntity row) => context.Entry(row).State = EntityState.Detached;
}
