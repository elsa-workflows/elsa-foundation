using System.Globalization;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core scheduler-work queue with bounded FIFO pages and fenced claims.</summary>
/// <remarks>
/// The item JSON is authoritative. Bounded projections are used only for scope, workflow, identity, and ordering;
/// every returned item is checked against those projections before it crosses the persistence boundary. Claim
/// transitions use the row revision for optimistic CAS, while consumption deliberately fences on owner and token so
/// a renewal remains consumable by its original claimant.
/// </remarks>
public sealed class EfSchedulerWorkQueueStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkClaimInspection
{
    private const string CursorPurpose = "ef-runtime-scheduler-work-v1";
    private const int MaxTransitionAttempts = 16;

    public bool SupportsClaimTransitions => true;

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(
        RuntimeSchedulerWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        ValidateItem(workItem);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workItem.WorkflowExecutionId, workItem.WorkItemId);
        var existing = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (existing is not null)
            return ReadChecked(existing, scope, workItem.WorkflowExecutionId, workItem.WorkItemId);

        var entity = ToEntity(workItem, scope, id, 1);
        context.SchedulerWorkItems.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(entity);
            return workItem;
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            Detach(entity);
            var winner = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
                         ?? throw new InvalidOperationException("The scheduler-work enqueue conflicted, but the winning row could not be reloaded.", exception);
            return ReadChecked(winner, scope, workItem.WorkflowExecutionId, workItem.WorkItemId);
        }
        catch (OperationCanceledException)
        {
            Detach(entity);
            throw;
        }
    }

    public async ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWorkflowExecutionId(query.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(query.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(query.WorkflowExecutionId);
        string? after = null;
        if (query.ContinuationToken is not null)
        {
            var cursor = DecodeCursor(query.ContinuationToken);
            if (cursor.Version != 1 || cursor.ScopeHash != scopeHash || cursor.WorkflowHash != workflowHash || string.IsNullOrWhiteSpace(cursor.OrderKey))
                throw new ArgumentException("The scheduler-work continuation belongs to another query or is invalid.", nameof(query));
            after = cursor.OrderKey;
        }

        var source = context.SchedulerWorkItems.AsNoTracking().Where(row =>
            row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
            row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash);
        if (after is not null)
            source = source.Where(row => row.WorkOrderKey.CompareTo(after) > 0);
        var rows = await source.OrderBy(row => row.WorkOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > query.Limit;
        if (hasNext)
            rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope, query.WorkflowExecutionId)).ToArray();
        var next = hasNext
            ? EncodeCursor(scopeHash, workflowHash, rows[^1].WorkOrderKey)
            : null;
        return new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, items, next);
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var row = await context.SchedulerWorkItems.AsNoTracking()
                .Where(candidate => candidate.ScopeKey == scopeKey && candidate.ScopeKeyHash == scopeHash &&
                                    candidate.WorkflowExecutionId == workflowKey && candidate.WorkflowExecutionIdHash == workflowHash)
                .OrderBy(candidate => candidate.WorkOrderKey)
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;
            var item = ReadChecked(row, scope, workflowExecutionId);
            AttachForDelete(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return item;
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(row);
            }
            catch (OperationCanceledException)
            {
                Detach(row);
                throw;
            }
        }

        throw TransitionDidNotSettle("dequeue", workflowExecutionId);
    }

    public async ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, workItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, workItemId);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var row = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                return false;
            _ = ReadChecked(row, scope, workflowExecutionId, workItemId);
            AttachForDelete(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(row);
            }
            catch (OperationCanceledException)
            {
                Detach(row);
                throw;
            }
        }

        throw TransitionDidNotSettle("delete", workflowExecutionId, workItemId);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        return await context.SchedulerWorkItems.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash)
            .Select(row => new { row.WorkflowExecutionId, row.WorkflowExecutionIdOrderKey })
            .Distinct()
            .OrderBy(row => row.WorkflowExecutionIdOrderKey)
            .ThenBy(row => row.WorkflowExecutionId)
            .Take(limit)
            .Select(row => EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorkflowExecutionId(request.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(request.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkflowExecutionId);
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var row = await context.SchedulerWorkItems.AsNoTracking()
                .Where(candidate => candidate.ScopeKey == scopeKey && candidate.ScopeKeyHash == scopeHash &&
                                    candidate.WorkflowExecutionId == workflowKey && candidate.WorkflowExecutionIdHash == workflowHash)
                .OrderBy(candidate => candidate.WorkOrderKey)
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;
            var item = ReadChecked(row, scope, request.WorkflowExecutionId);
            if (row.VisibleAfterUtcTicks is { } visibleAfter && visibleAfter > request.Now.UtcTicks)
                return null;

            var originalRevision = row.Revision;
            var updated = ToClaimedEntity(row, request);
            AttachForUpdate(updated, originalRevision);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                Detach(updated);
                return ToClaim(updated, item);
            }
            catch (DbUpdateConcurrencyException)
            {
                Detach(updated);
            }
            catch (OperationCanceledException)
            {
                Detach(updated);
                throw;
            }
        }

        throw TransitionDidNotSettle("claim", request.WorkflowExecutionId);
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Scheduler work visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadClaimAsync(claim, scope, cancellationToken);
        if (row is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        var item = ReadChecked(row, scope, claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        if (!Matches(row, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        var originalRevision = row.Revision;
        var updated = ToRenewedEntity(row, now, visibilityTimeout);
        AttachForUpdate(updated, originalRevision);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(updated);
            return RuntimeSchedulerWorkClaimTransitionResult.Applied(ToClaim(updated, item));
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(updated);
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(updated);
            throw;
        }
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadClaimAsync(claim, scope, cancellationToken);
        if (row is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        _ = ReadChecked(row, scope, claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        if (!Matches(row, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        AttachForDelete(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return RuntimeSchedulerWorkClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(row);
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaim(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadClaimAsync(claim, scope, cancellationToken);
        if (row is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        _ = ReadChecked(row, scope, claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        if (!Matches(row, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        row.ClaimOwnerId = null;
        row.ClaimedAtUtcTicks = null;
        row.ClaimedAtOffsetMinutes = null;
        row.VisibleAfterUtcTicks = visibleAt.UtcTicks;
        row.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAt);
        row.Revision = checked(row.Revision + 1);
        AttachForUpdate(row, claim.Revision);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            Detach(row);
            return RuntimeSchedulerWorkClaimTransitionResult.Applied();
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach(row);
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        }
        catch (OperationCanceledException)
        {
            Detach(row);
            throw;
        }
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        ValidateIdentity(consumed.WorkflowExecutionId, consumed.WorkItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumed.ClaimOwnerId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, consumed.WorkflowExecutionId, consumed.WorkItemId);
        var row = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;
        _ = ReadChecked(row, scope, consumed.WorkflowExecutionId, consumed.WorkItemId);
        if (row.ClaimOwnerId is null ||
            !StringComparer.Ordinal.Equals(row.ClaimOwnerId, EfRuntimeOperationalStoreSupport.Encode(consumed.ClaimOwnerId)) ||
            row.ClaimToken != consumed.FencingToken)
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        // ExecuteDelete is the provider-neutral atomic owner+token fence. It intentionally does not include Revision:
        // renewal advances Revision but preserves the claim fence, while a successor advances ClaimToken.
        var deleted = await context.SchedulerWorkItems
            .Where(candidate => candidate.Id == id && candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                                candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(consumed.WorkflowExecutionId) &&
                                candidate.ClaimOwnerId == EfRuntimeOperationalStoreSupport.Encode(consumed.ClaimOwnerId) &&
                                candidate.ClaimToken == consumed.FencingToken)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1
            ? RuntimeSchedulerWorkClaimTransitionResult.Applied()
            : RuntimeSchedulerWorkClaimTransitionResult.Stale;
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(
        string workflowExecutionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var rows = await context.SchedulerWorkItems.AsNoTracking()
            .Where(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
                          row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash &&
                          row.ClaimOwnerId != null && row.ClaimedAtUtcTicks != null &&
                          row.VisibleAfterUtcTicks != null && row.VisibleAfterUtcTicks > now.UtcTicks)
            .OrderBy(row => row.WorkOrderKey)
            .ToArrayAsync(cancellationToken);
        return rows.Select(row =>
        {
            var item = ReadChecked(row, scope, workflowExecutionId);
            return ToClaim(row, item);
        }).ToArray();
    }

    private async Task<SchedulerWorkItemEntity?> LoadClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        return await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    private static SchedulerWorkItemEntity ToEntity(RuntimeSchedulerWorkItem item, string scope, string id, long revision) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(item.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(item.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(item.WorkflowExecutionId),
        WorkItemId = EfRuntimeOperationalStoreSupport.Encode(item.WorkItemId),
        WorkItemIdHash = EfRuntimeOperationalStoreSupport.Hash(item.WorkItemId),
        WorkOrderKey = WorkOrderKey(item),
        EnqueuedAtUtcTicks = item.EnqueuedAt.UtcTicks,
        EnqueuedAtOffsetMinutes = OffsetMinutes(item.EnqueuedAt),
        RecordedAtUtcTicks = item.RecordedAt.UtcTicks,
        RecordedAtOffsetMinutes = OffsetMinutes(item.RecordedAt),
        ContentJson = RuntimeArtifactJson.Serialize(item),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = revision
    };

    private static SchedulerWorkItemEntity ToClaimedEntity(SchedulerWorkItemEntity row, RuntimeSchedulerWorkClaimRequest request)
    {
        var visibleAfter = request.Now.Add(request.VisibilityTimeout);
        row.ClaimOwnerId = EfRuntimeOperationalStoreSupport.Encode(request.OwnerId);
        row.ClaimToken = checked(row.ClaimToken + 1);
        row.ClaimedAtUtcTicks = request.Now.UtcTicks;
        row.ClaimedAtOffsetMinutes = OffsetMinutes(request.Now);
        row.VisibleAfterUtcTicks = visibleAfter.UtcTicks;
        row.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAfter);
        row.Revision = checked(row.Revision + 1);
        return row;
    }

    private static SchedulerWorkItemEntity ToRenewedEntity(SchedulerWorkItemEntity row, DateTimeOffset now, TimeSpan visibilityTimeout)
    {
        var visibleAfter = now.Add(visibilityTimeout);
        row.VisibleAfterUtcTicks = visibleAfter.UtcTicks;
        row.VisibleAfterOffsetMinutes = OffsetMinutes(visibleAfter);
        row.Revision = checked(row.Revision + 1);
        return row;
    }

    private static RuntimeSchedulerWorkClaim ToClaim(SchedulerWorkItemEntity row, RuntimeSchedulerWorkItem item)
    {
        ValidateClaimProjection(row);
        return new RuntimeSchedulerWorkClaim(
            item,
            EfRuntimeOperationalStoreSupport.Decode(row.ClaimOwnerId!),
            row.ClaimToken,
            row.Revision,
            FromUtcTicks(row.ClaimedAtUtcTicks!.Value, row.ClaimedAtOffsetMinutes!.Value),
            FromUtcTicks(row.VisibleAfterUtcTicks!.Value, row.VisibleAfterOffsetMinutes!.Value));
    }

    private static bool Matches(SchedulerWorkItemEntity row, RuntimeSchedulerWorkClaim claim) =>
        row.Revision == claim.Revision &&
        row.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(row.ClaimOwnerId, EfRuntimeOperationalStoreSupport.Encode(claim.OwnerId));

    internal static RuntimeSchedulerWorkItem ReadChecked(
        SchedulerWorkItemEntity row,
        string scope,
        string? expectedWorkflowExecutionId = null,
        string? expectedWorkItemId = null)
    {
        if (row.Revision <= 0 || row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The scheduler-work row scope, schema, or revision projection is corrupt.");

        RuntimeSchedulerWorkItem item;
        try
        {
            item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(row.ContentJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted scheduler-work item is not valid current data.", exception);
        }
        ValidateItem(item);
        ValidateClaimProjection(row);
        if ((expectedWorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(expectedWorkflowExecutionId, item.WorkflowExecutionId)) ||
            (expectedWorkItemId is not null && !StringComparer.Ordinal.Equals(expectedWorkItemId, item.WorkItemId)) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, item.WorkflowExecutionId, item.WorkItemId) ||
            row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(item.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(item.WorkflowExecutionId) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(item.WorkflowExecutionId) ||
            row.WorkItemId != EfRuntimeOperationalStoreSupport.Encode(item.WorkItemId) ||
            row.WorkItemIdHash != EfRuntimeOperationalStoreSupport.Hash(item.WorkItemId) ||
            row.WorkOrderKey != WorkOrderKey(item) ||
            row.EnqueuedAtUtcTicks != item.EnqueuedAt.UtcTicks ||
            row.EnqueuedAtOffsetMinutes != OffsetMinutes(item.EnqueuedAt) ||
            row.RecordedAtUtcTicks != item.RecordedAt.UtcTicks ||
            row.RecordedAtOffsetMinutes != OffsetMinutes(item.RecordedAt))
            throw new InvalidDataException("The scheduler-work row identity or ordering projection does not match its current content.");
        return item;
    }

    private static void ValidateClaimProjection(SchedulerWorkItemEntity row)
    {
        if (row.ClaimToken < 0)
            throw new InvalidDataException("The scheduler-work claim token cannot be negative.");
        if (row.ClaimToken == 0 && (row.ClaimOwnerId is not null || row.ClaimedAtUtcTicks is not null || row.ClaimedAtOffsetMinutes is not null || row.VisibleAfterUtcTicks is not null || row.VisibleAfterOffsetMinutes is not null))
            throw new InvalidDataException("The scheduler-work initial claim state is inconsistent.");
        if (row.ClaimToken > 0 && row.ClaimOwnerId is null && (row.ClaimedAtUtcTicks is not null || row.ClaimedAtOffsetMinutes is not null || row.VisibleAfterUtcTicks is null || row.VisibleAfterOffsetMinutes is null))
            throw new InvalidDataException("The scheduler-work released claim state is inconsistent.");
        if (row.ClaimToken > 0 && row.ClaimOwnerId is not null && (row.ClaimedAtUtcTicks is null || row.ClaimedAtOffsetMinutes is null || row.VisibleAfterUtcTicks is null || row.VisibleAfterOffsetMinutes is null))
            throw new InvalidDataException("The scheduler-work claim state is incomplete.");
        if (row.ClaimedAtUtcTicks is { } claimed && row.VisibleAfterUtcTicks is { } visible && visible <= claimed)
            throw new InvalidDataException("The scheduler-work visibility deadline must follow the claim time.");
    }

    private void AttachForUpdate(SchedulerWorkItemEntity row, long originalRevision)
    {
        DetachTracked(row.Id);
        context.SchedulerWorkItems.Attach(row);
        context.Entry(row).Property(entity => entity.Revision).OriginalValue = originalRevision;
        context.Entry(row).State = EntityState.Modified;
    }

    private void AttachForDelete(SchedulerWorkItemEntity row)
    {
        DetachTracked(row.Id);
        context.SchedulerWorkItems.Attach(row);
        context.Entry(row).State = EntityState.Deleted;
    }

    private void DetachTracked(string id)
    {
        var tracked = context.ChangeTracker.Entries<SchedulerWorkItemEntity>().SingleOrDefault(entry => entry.Entity.Id == id);
        tracked?.State = EntityState.Detached;
    }

    private void Detach(SchedulerWorkItemEntity row) => context.Entry(row).State = EntityState.Detached;

    private static void ValidateClaim(RuntimeSchedulerWorkClaim claim)
    {
        ValidateItem(claim.Item);
        ArgumentException.ThrowIfNullOrWhiteSpace(claim.OwnerId);
        if (claim.FencingToken <= 0 || claim.Revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(claim), "Scheduler-work claim token and revision must be positive.");
    }

    private static void ValidateItem(RuntimeSchedulerWorkItem? item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateWorkflowExecutionId(item.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.WorkItemId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string workItemId)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
    }

    private static void ValidateWorkflowExecutionId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (workflowExecutionId.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", nameof(workflowExecutionId));
    }

    private static string WorkOrderKey(RuntimeSchedulerWorkItem item) =>
        string.Concat(
            EfRuntimeOperationalStoreSupport.Hash(item.WorkflowExecutionId), ".",
            item.RecordedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture), ".",
            (item.Sequence ?? long.MaxValue).ToString("D20", CultureInfo.InvariantCulture), ".",
            EfRuntimeOperationalStoreSupport.Hash(item.WorkItemId));

    private string EncodeCursor(string scopeHash, string workflowHash, string orderKey) =>
        continuationCodec.Encode(CursorPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new QueueCursor(1, scopeHash, workflowHash, orderKey))));

    private QueueCursor DecodeCursor(string token)
    {
        try
        {
            return RuntimeArtifactJson.Deserialize<QueueCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(CursorPurpose, token)));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new ArgumentException("The scheduler-work continuation is invalid.", nameof(token), exception);
        }
    }

    private sealed record QueueCursor(int Version, string ScopeHash, string WorkflowHash, string OrderKey);

    private static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    private static DateTimeOffset FromUtcTicks(long utcTicks, int offsetMinutes) =>
        new DateTimeOffset(new DateTime(utcTicks, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    private static InvalidOperationException TransitionDidNotSettle(string transition, string workflowExecutionId, string? workItemId = null) =>
        new($"Scheduler-work {transition} for workflow execution '{workflowExecutionId}'" +
            (workItemId is null ? string.Empty : $" and work item '{workItemId}'") +
            $" did not settle after {MaxTransitionAttempts} compare-and-swap attempts.");
}
