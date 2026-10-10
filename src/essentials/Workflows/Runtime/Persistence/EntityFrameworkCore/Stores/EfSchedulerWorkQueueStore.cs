using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
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
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkClaimInspection
{
    private const string CursorPurpose = "ef-runtime-scheduler-work-v1";
    private static readonly EfWriteRetry Transitions = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Concurrency);

    // A claim may take a row at an instant only while the row is visible then: never claimed, released, or under a
    // lapsed claim. Stated once: discovery filters with it in the query and ClaimAsync checks the head with the compiled
    // form, so the two cannot drift.
    private static readonly Expression<Func<SchedulerWorkItemEntity, long, bool>> VisibleAtTicks =
        (row, nowUtcTicks) => row.VisibleAfterUtcTicks == null || row.VisibleAfterUtcTicks <= nowUtcTicks;
    private static readonly Func<SchedulerWorkItemEntity, long, bool> IsVisibleAt = VisibleAtTicks.Compile();

    public bool SupportsClaimTransitions => true;

    public bool SupportsTargetedDeletion => true;

    public bool SupportsClaimableBacklogDiscovery => true;

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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
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
        var scope = CurrentScope();
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(query.WorkflowExecutionId);
        string? after = null;
        if (query.ContinuationToken is not null)
        {
            var cursor = DecodeCursor(query.ContinuationToken);
            if (cursor.Version != 1 || cursor.ScopeHash != scope.Hash || cursor.WorkflowHash != workflowHash || string.IsNullOrWhiteSpace(cursor.OrderKey))
                throw new ArgumentException("The scheduler-work continuation belongs to another query or is invalid.", nameof(query));
            after = cursor.OrderKey;
        }

        var source = scope.RowsOf(query.WorkflowExecutionId);
        if (after is not null)
            source = source.Where(row => row.WorkOrderKey.CompareTo(after) > 0);
        var rows = await source.OrderBy(row => row.WorkOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > query.Limit;
        if (hasNext)
            rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope.Value, query.WorkflowExecutionId)).ToArray();
        var next = hasNext
            ? EncodeCursor(scope.Hash, workflowHash, rows[^1].WorkOrderKey)
            : null;
        return new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, items, next);
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = CurrentScope();
        var rows = scope.RowsOf(workflowExecutionId);
        return await Transitions.RunAsync<RuntimeSchedulerWorkItem?>(context, async () =>
        {
            var row = await rows.OrderBy(candidate => candidate.WorkOrderKey).FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;
            var item = ReadChecked(row, scope.Value, workflowExecutionId);
            AttachForDelete(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return item;
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is OperationCanceledException)
            {
                Detach(row);
                throw;
            }
        }, _ => throw TransitionDidNotSettle("dequeue", workflowExecutionId), cancellationToken);
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
        return await Transitions.RunAsync(context, async () =>
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
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is OperationCanceledException)
            {
                Detach(row);
                throw;
            }
        }, _ => throw TransitionDidNotSettle("delete", workflowExecutionId, workItemId), cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        return await CurrentScope().Rows
            .Select(row => new { row.WorkflowExecutionId, row.WorkflowExecutionIdOrderKey })
            .Distinct()
            .OrderBy(row => row.WorkflowExecutionIdOrderKey)
            .ThenBy(row => row.WorkflowExecutionId)
            .Take(limit)
            .Select(row => EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
        RuntimeSchedulerClaimableBacklogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AfterWorkflowExecutionId is { } after)
            ValidateWorkflowExecutionId(after);
        cancellationToken.ThrowIfCancellationRequested();

        // One row per execution qualifies: its head, and only while a claim at Now could take it. Each execution's
        // order key is unique and ordinal-preserving, so it alone is a total keyset order.
        var claimable = Heads(CurrentScope().Rows).Where(VisibleAt(query.Now));
        if (query.AfterWorkflowExecutionId is not null)
        {
            var afterOrderKey = EfRuntimeOperationalStoreSupport.Order(query.AfterWorkflowExecutionId);
            claimable = claimable.Where(row => row.WorkflowExecutionIdOrderKey.CompareTo(afterOrderKey) > 0);
        }

        return await claimable
            .OrderBy(row => row.WorkflowExecutionIdOrderKey)
            .Take(query.Limit)
            .Select(row => EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
        IReadOnlyCollection<string> workflowExecutionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionIds);
        foreach (var workflowExecutionId in workflowExecutionIds)
            ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = CurrentScope();
        var heads = Heads(scope.Rows);
        var rows = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            workflowExecutionIds,
            batch =>
            {
                var hashes = batch.Select(EfRuntimeOperationalStoreSupport.Hash).ToArray();
                var keys = batch.Select(EfRuntimeOperationalStoreSupport.Encode).ToArray();
                return heads.Where(row => hashes.Contains(row.WorkflowExecutionIdHash) && keys.Contains(row.WorkflowExecutionId));
            },
            row => EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId),
            cancellationToken);
        return rows.ToDictionary(
            entry => entry.Key,
            entry => ReadChecked(entry.Value, scope.Value, entry.Key),
            StringComparer.Ordinal);
    }

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorkflowExecutionId(request.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = CurrentScope();
        var rows = scope.RowsOf(request.WorkflowExecutionId);
        return await Transitions.RunAsync<RuntimeSchedulerWorkClaim?>(context, async () =>
        {
            var row = await rows.OrderBy(candidate => candidate.WorkOrderKey).FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;
            var item = ReadChecked(row, scope.Value, request.WorkflowExecutionId);
            if (!IsVisibleAt(row, request.Now.UtcTicks))
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
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception) || exception is OperationCanceledException)
            {
                Detach(updated);
                throw;
            }
        }, _ => throw TransitionDidNotSettle("claim", request.WorkflowExecutionId), cancellationToken);
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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
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
        (row.ClaimedAtUtcTicks, row.ClaimedAtOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(null);
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(visibleAt);
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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
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
        var scope = CurrentScope();
        var workflowRows = scope.RowsOf(workflowExecutionId);
        var claims = new List<RuntimeSchedulerWorkClaim>();
        string? after = null;
        do
        {
            var source = workflowRows.Where(row =>
                row.ClaimOwnerId != null && row.ClaimedAtUtcTicks != null &&
                row.VisibleAfterUtcTicks != null && row.VisibleAfterUtcTicks > now.UtcTicks);
            if (after is not null)
                source = source.Where(row => row.WorkOrderKey.CompareTo(after) > 0);

            var rows = await source
                .OrderBy(row => row.WorkOrderKey)
                .Take(checked(RuntimeStorePageRequest.MaximumLimit + 1))
                .ToArrayAsync(cancellationToken);
            var hasNext = rows.Length > RuntimeStorePageRequest.MaximumLimit;
            var selected = hasNext ? rows[..RuntimeStorePageRequest.MaximumLimit] : rows;
            foreach (var row in selected)
            {
                var item = ReadChecked(row, scope.Value, workflowExecutionId);
                claims.Add(ToClaim(row, item));
            }

            after = hasNext ? selected[^1].WorkOrderKey : null;
        } while (after is not null);

        return claims;
    }

    // The scope every query is confined to, resolved once per call: the raw scope for checked reads, its hash for
    // continuations, and its rows.
    private StoreScope CurrentScope()
    {
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var key = EfRuntimeOperationalStoreSupport.Encode(scope);
        var hash = EfRuntimeOperationalStoreSupport.Hash(scope);
        return new StoreScope(scope, hash, context.SchedulerWorkItems.AsNoTracking().Where(row => row.ScopeKey == key && row.ScopeKeyHash == hash));
    }

    private sealed record StoreScope(string Value, string Hash, IQueryable<SchedulerWorkItemEntity> Rows)
    {
        public IQueryable<SchedulerWorkItemEntity> RowsOf(string workflowExecutionId)
        {
            var key = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
            var hash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
            return Rows.Where(row => row.WorkflowExecutionId == key && row.WorkflowExecutionIdHash == hash);
        }
    }

    // Each execution's head: the row ClaimAsync takes, the one with the lowest WorkOrderKey.
    private static IQueryable<SchedulerWorkItemEntity> Heads(IQueryable<SchedulerWorkItemEntity> rows) =>
        rows.Where(row => !rows.Any(earlier =>
            earlier.WorkflowExecutionIdHash == row.WorkflowExecutionIdHash &&
            earlier.WorkflowExecutionId == row.WorkflowExecutionId &&
            earlier.WorkOrderKey.CompareTo(row.WorkOrderKey) < 0));

    // VisibleAtTicks with the instant bound as a captured field, the way a closure binds it, so EF sends it as a
    // parameter rather than a literal and keeps a single cached query.
    private static Expression<Func<SchedulerWorkItemEntity, bool>> VisibleAt(DateTimeOffset now)
    {
        var instant = Expression.Field(Expression.Constant(new Instant(now.UtcTicks)), nameof(Instant.UtcTicks));
        var body = new ParameterReplacer(VisibleAtTicks.Parameters[1], instant).Visit(VisibleAtTicks.Body);
        return Expression.Lambda<Func<SchedulerWorkItemEntity, bool>>(body, VisibleAtTicks.Parameters[0]);
    }

    private sealed class Instant(long utcTicks)
    {
        public readonly long UtcTicks = utcTicks;
    }

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
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
        EnqueuedAtOffsetMinutes = EfRuntimeOperationalStoreSupport.OffsetMinutes(item.EnqueuedAt),
        RecordedAtUtcTicks = item.RecordedAt.UtcTicks,
        RecordedAtOffsetMinutes = EfRuntimeOperationalStoreSupport.OffsetMinutes(item.RecordedAt),
        ContentJson = RuntimeArtifactJson.Serialize(item),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = revision
    };

    private static SchedulerWorkItemEntity ToClaimedEntity(SchedulerWorkItemEntity row, RuntimeSchedulerWorkClaimRequest request)
    {
        var visibleAfter = request.Now.Add(request.VisibilityTimeout);
        row.ClaimOwnerId = EfRuntimeOperationalStoreSupport.Encode(request.OwnerId);
        row.ClaimToken = checked(row.ClaimToken + 1);
        (row.ClaimedAtUtcTicks, row.ClaimedAtOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(request.Now);
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(visibleAfter);
        row.Revision = checked(row.Revision + 1);
        return row;
    }

    private static SchedulerWorkItemEntity ToRenewedEntity(SchedulerWorkItemEntity row, DateTimeOffset now, TimeSpan visibilityTimeout)
    {
        var visibleAfter = now.Add(visibilityTimeout);
        (row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = EfRuntimeOperationalStoreSupport.TimestampColumns(visibleAfter);
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
            EfRuntimeOperationalStoreSupport.FromUtcTicks(row.ClaimedAtUtcTicks!.Value, row.ClaimedAtOffsetMinutes!.Value),
            EfRuntimeOperationalStoreSupport.FromUtcTicks(row.VisibleAfterUtcTicks!.Value, row.VisibleAfterOffsetMinutes!.Value));
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
        if (EfSchemaVersion.NotReadable(RuntimeOperationalStateEfModule.Chain, row.SchemaVersion) || row.Revision <= 0 ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The scheduler-work row scope, schema, or revision projection is corrupt.");

        RuntimeSchedulerWorkItem item;
        try
        {
            item = RuntimeArtifactJson.Deserialize<RuntimeSchedulerWorkItem>(RuntimeOperationalStateEfModule.Chain.Upcast<SchedulerWorkItemEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
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
            row.EnqueuedAtOffsetMinutes != EfRuntimeOperationalStoreSupport.OffsetMinutes(item.EnqueuedAt) ||
            row.RecordedAtUtcTicks != item.RecordedAt.UtcTicks ||
            row.RecordedAtOffsetMinutes != EfRuntimeOperationalStoreSupport.OffsetMinutes(item.RecordedAt))
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
            StableOrderHash(item.WorkflowExecutionId), ".",
            item.RecordedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture), ".",
            (item.Sequence ?? long.MaxValue).ToString("D20", CultureInfo.InvariantCulture), ".",
            StableOrderHash(item.WorkItemId));

    // The queue ordering is part of the provider-neutral contract. Keep this separate from
    // EfRelationalIdentity.Hash, whose UTF-16/uppercase representation is the physical identity format.
    private static string StableOrderHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

    private static InvalidOperationException TransitionDidNotSettle(string transition, string workflowExecutionId, string? workItemId = null) =>
        new($"Scheduler-work {transition} for workflow execution '{workflowExecutionId}'" +
            (workItemId is null ? string.Empty : $" and work item '{workItemId}'") +
            $" did not settle after {Transitions.MaxAttempts} compare-and-swap attempts.");
}
