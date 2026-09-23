using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core execution-liveness store with provider-neutral CAS and recovery paging (R16).</summary>
public sealed class EfExecutionLivenessStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IExecutionLivenessStateStore, IRuntimeRecoveryLivenessPageSource
{
    private const string RecoveryCursorPurpose = "ef-runtime-recovery-liveness-v1";
    private const string GlobalCursorPurpose = "ef-runtime-liveness-global-v1";
    private const string WorkflowCursorPurpose = "ef-runtime-liveness-workflow-v1";

    public async ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var entity = await LoadAsync(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId, tracking: true, cancellationToken);
        if (entity is null)
            context.ExecutionLivenessStates.Add(ToEntity(state, scope, 1));
        else
        {
            _ = Read(entity, scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId);
            Copy(entity, state, scope, checked(entity.Revision + 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The execution-liveness state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The execution-liveness state changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        context.ChangeTracker.Clear();
        var entity = await LoadAsync(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId, tracking: true, cancellationToken);

        if (expectedRevision == 0)
        {
            if (entity is not null)
                return new(ExecutionLivenessStateWriteStatus.RevisionConflict, entity.Revision);
            context.ExecutionLivenessStates.Add(ToEntity(state, scope, 1));
        }
        else
        {
            if (entity is null)
                return new(ExecutionLivenessStateWriteStatus.NotFound);
            _ = Read(entity, scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId);
            if (entity.Revision != expectedRevision)
                return new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.RevisionConflict, entity.Revision);
            Copy(entity, state, scope, checked(expectedRevision + 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(ExecutionLivenessStateWriteStatus.Saved, expectedRevision + 1);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return new(ExecutionLivenessStateWriteStatus.RevisionConflict);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            return new(ExecutionLivenessStateWriteStatus.RevisionConflict);
        }
    }

    public async ValueTask<ExecutionLivenessState?> FindAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentityPair(workflowExecutionId, operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, workflowExecutionId, operationalStateId, tracking: false, cancellationToken);
        return row is null ? null : Read(row, scope, workflowExecutionId, operationalStateId);
    }

    public async ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentityPair(workflowExecutionId, operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await LoadAsync(scope, workflowExecutionId, operationalStateId, tracking: false, cancellationToken);
        if (row is null)
            return null;
        var state = Read(row, scope, workflowExecutionId, operationalStateId);
        return new VersionedExecutionLivenessState(state, row.Revision);
    }

    public async ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListPageAsync(
        ExecutionLivenessStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentity(query.WorkflowExecutionId, nameof(query.WorkflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var cursor = DecodeIdentityCursor(query.ContinuationToken, WorkflowCursorPurpose, scope, query.WorkflowExecutionId, query);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflow = EfRuntimeOperationalStoreSupport.Encode(query.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(query.WorkflowExecutionId);
        var source = context.ExecutionLivenessStates.AsNoTracking().Where(row =>
            row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            row.WorkflowExecutionIdHash == workflowHash && row.WorkflowExecutionId == workflow);
        if (cursor is not null)
            source = source.Where(row => string.Compare(row.OperationalStateIdOrderKey, EfRuntimeOperationalStoreSupport.Order(cursor.Last)) > 0);
        var rows = await source.OrderBy(row => row.OperationalStateIdOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > query.Limit;
        if (hasMore)
            rows = rows[..query.Limit];
        var items = rows.Select(row => Read(row, scope, query.WorkflowExecutionId)).ToArray();
        var next = hasMore ? EncodeIdentityCursor(scope, query.WorkflowExecutionId, items[^1].ExecutionLivenessStateId, WorkflowCursorPurpose) : null;
        return new RuntimeStorePage<ExecutionLivenessState>(query, items, next);
    }

    public async ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var cursor = DecodeGlobalCursor(query.ContinuationToken, scope, query);
        var source = context.ExecutionLivenessStates.AsNoTracking().Where(row =>
            row.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            row.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope));
        if (cursor is not null)
        {
            var workflowOrder = EfRuntimeOperationalStoreSupport.Order(cursor.WorkflowExecutionId);
            var operationalOrder = EfRuntimeOperationalStoreSupport.Order(cursor.OperationalStateId);
            source = source.Where(row =>
                string.Compare(row.WorkflowExecutionIdOrderKey, workflowOrder) > 0 ||
                row.WorkflowExecutionIdOrderKey == workflowOrder && string.Compare(row.OperationalStateIdOrderKey, operationalOrder) > 0);
        }
        var rows = await source.OrderBy(row => row.WorkflowExecutionIdOrderKey).ThenBy(row => row.OperationalStateIdOrderKey)
            .Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > query.Limit;
        if (hasMore)
            rows = rows[..query.Limit];
        var items = rows.Select(row => Read(row, scope)).ToArray();
        var next = hasMore ? EncodeGlobalCursor(scope, items[^1]) : null;
        return new RuntimeStorePage<ExecutionLivenessState>(query, items, next);
    }

    /// <summary>
    /// Executes a bounded union of the indexed interruption, lease, and heartbeat routes. Each route reads at most
    /// one requested page plus a look-ahead row; the final page is ordered by the provider-neutral earliest due time.
    /// </summary>
    public async ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListRecoveryPageAsync(
        RuntimeRecoveryScanRequest request,
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var binding = RecoveryBinding(scope, request);
        var cursor = DecodeRecoveryCursor(query.ContinuationToken, binding, query);
        var rows = new Dictionary<string, ExecutionLivenessStateEntity>(StringComparer.Ordinal);
        var routeCount = request.OwnerId is null ? 4 : 6;
        for (var route = 0; route < routeCount; route++)
        {
            RoutePageCursor? routeCursor = null;
            var usefulCount = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var routeRows = await QueryRecoveryRouteAsync(scope, request, route, cursor, routeCursor, query.Limit, cancellationToken);
                foreach (var row in routeRows)
                {
                    var state = Read(row, scope);
                    var eligibleAt = RuntimeRecoveryCandidateSelector.GetEligibleAt(state, request);
                    if (eligibleAt is null || cursor is not null && Compare(eligibleAt.Value, state, cursor) <= 0)
                        continue;
                    usefulCount++;
                    var identity = Identity(state.WorkflowExecutionId, state.ExecutionLivenessStateId);
                    if (!rows.ContainsKey(identity))
                        rows.Add(identity, row);
                }

                // A route can contain rows that are due on this route but are already represented by an earlier
                // signal on another route. Advance its provider keyset until it has enough useful candidates or is
                // exhausted; every round-trip remains limited to the requested page plus one look-ahead row.
                if (routeRows.Length <= query.Limit || usefulCount > query.Limit)
                    break;
                var last = routeRows[^1];
                routeCursor = CreateRouteCursor(last, route, request.OwnerId is not null);
            }
        }

        var ordered = rows.Values
            .Select(row => Read(row, scope))
            .Select(state => (State: state, EligibleAt: RuntimeRecoveryCandidateSelector.GetEligibleAt(state, request)))
            .Where(item => item.EligibleAt is not null && (cursor is null || Compare(item.EligibleAt.Value, item.State, cursor) > 0))
            .OrderBy(item => item.EligibleAt)
            .ThenBy(item => item.State.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(item => item.State.ExecutionLivenessStateId, StringComparer.Ordinal)
            .ToArray();
        var hasMore = ordered.Length > query.Limit;
        var items = ordered.Take(query.Limit).Select(item => item.State).ToArray();
        var next = hasMore ? EncodeRecoveryCursor(binding, ordered[query.Limit - 1].EligibleAt!.Value, ordered[query.Limit - 1].State) : null;
        return new RuntimeStorePage<ExecutionLivenessState>(query, items, next);
    }

    private async Task<ExecutionLivenessStateEntity[]> QueryRecoveryRouteAsync(
        string scope,
        RuntimeRecoveryScanRequest request,
        int route,
        RecoveryCursor? cursor,
        RoutePageCursor? routeCursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var source = context.ExecutionLivenessStates.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey);
        var owner = request.OwnerId is null ? null : EfRuntimeOperationalStoreSupport.Encode(request.OwnerId);
        var detected = (int)RuntimeInterruptionStatus.Detected;
        switch (route)
        {
            case 0 when owner is null:
                source = source.Where(row => row.InterruptedStatus == detected);
                break;
            case 1 when owner is null:
                source = source.Where(row => row.LeaseExpiresAtUtcTicks != null && row.LeaseExpiresAtUtcTicks <= request.Now.UtcTicks);
                break;
            case 2 when owner is null:
                source = source.Where(row => row.LeaseAcquiredAtUtcTicks != null && row.LeaseAcquiredAtUtcTicks <= (request.Now - request.LeaseTimeout).UtcTicks);
                break;
            case 3 when owner is null:
                source = source.Where(row => row.HeartbeatRecordedAtUtcTicks != null && row.HeartbeatRecordedAtUtcTicks <= (request.Now - request.HeartbeatTimeout).UtcTicks);
                break;
            case 0:
                source = source.Where(row => row.InterruptedStatus == detected && row.LeaseOwnerId == owner);
                break;
            case 1:
                source = source.Where(row => row.InterruptedStatus == detected && row.HeartbeatOwnerId == owner);
                break;
            case 2:
                source = source.Where(row => row.InterruptedStatus == detected && !row.HasOperationalOwner);
                break;
            case 3:
                source = source.Where(row => row.LeaseOwnerId == owner && row.LeaseExpiresAtUtcTicks != null && row.LeaseExpiresAtUtcTicks <= request.Now.UtcTicks);
                break;
            case 4:
                source = source.Where(row => row.LeaseOwnerId == owner && row.LeaseAcquiredAtUtcTicks != null && row.LeaseAcquiredAtUtcTicks <= (request.Now - request.LeaseTimeout).UtcTicks);
                break;
            case 5:
                source = source.Where(row => row.HeartbeatOwnerId == owner && row.HeartbeatRecordedAtUtcTicks != null && row.HeartbeatRecordedAtUtcTicks <= (request.Now - request.HeartbeatTimeout).UtcTicks);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }

        // A recovery cursor is ordered by the earliest signal, not by identity. A plain identity lower bound would
        // incorrectly discard a row whose identity sorts before the cursor but whose due time is later. Every route
        // therefore applies its own due-time lower bound (the final selector still handles overlapping routes), with
        // identity used only to break ties at the cursor timestamp.
        var isInterruptionRoute = request.OwnerId is not null ? route <= 2 : route == 0;
        var isLeaseExpiryRoute = request.OwnerId is null ? route == 1 : route == 3;
        var isLeaseAcquiredRoute = request.OwnerId is null ? route == 2 : route == 4;
        if (cursor is not null)
        {
            var cursorWorkflow = EfRuntimeOperationalStoreSupport.Order(cursor.WorkflowExecutionId);
            var cursorOperational = EfRuntimeOperationalStoreSupport.Order(cursor.OperationalStateId);
            var cursorTicks = cursor.EligibleAtUtcTicks;
            if (isInterruptionRoute)
            {
                source = source.Where(row =>
                    row.InterruptedAtUtcTicks > cursorTicks ||
                    row.InterruptedAtUtcTicks == cursorTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(cursorWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == cursorWorkflow && row.OperationalStateIdOrderKey.CompareTo(cursorOperational) > 0));
            }
            else if (isLeaseExpiryRoute)
            {
                source = source.Where(row =>
                    row.LeaseExpiresAtUtcTicks > cursorTicks ||
                    row.LeaseExpiresAtUtcTicks == cursorTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(cursorWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == cursorWorkflow && row.OperationalStateIdOrderKey.CompareTo(cursorOperational) > 0));
            }
            else if (isLeaseAcquiredRoute)
            {
                var cursorAcquiredAtTicks = checked(cursorTicks - request.LeaseTimeout.Ticks);
                source = source.Where(row =>
                    row.LeaseAcquiredAtUtcTicks > cursorAcquiredAtTicks ||
                    row.LeaseAcquiredAtUtcTicks == cursorAcquiredAtTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(cursorWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == cursorWorkflow && row.OperationalStateIdOrderKey.CompareTo(cursorOperational) > 0));
            }
            else
            {
                var cursorHeartbeatRecordedAtTicks = checked(cursorTicks - request.HeartbeatTimeout.Ticks);
                source = source.Where(row =>
                    row.HeartbeatRecordedAtUtcTicks > cursorHeartbeatRecordedAtTicks ||
                    row.HeartbeatRecordedAtUtcTicks == cursorHeartbeatRecordedAtTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(cursorWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == cursorWorkflow && row.OperationalStateIdOrderKey.CompareTo(cursorOperational) > 0));
            }
        }

        if (routeCursor is not null)
        {
            var routeWorkflow = EfRuntimeOperationalStoreSupport.Order(routeCursor.WorkflowExecutionId);
            var routeOperational = EfRuntimeOperationalStoreSupport.Order(routeCursor.OperationalStateId);
            if (isInterruptionRoute)
            {
                source = source.Where(row =>
                    row.InterruptedAtUtcTicks > routeCursor.SignalUtcTicks ||
                    row.InterruptedAtUtcTicks == routeCursor.SignalUtcTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(routeWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == routeWorkflow && row.OperationalStateIdOrderKey.CompareTo(routeOperational) > 0));
            }
            else if (isLeaseExpiryRoute)
            {
                source = source.Where(row =>
                    row.LeaseExpiresAtUtcTicks > routeCursor.SignalUtcTicks ||
                    row.LeaseExpiresAtUtcTicks == routeCursor.SignalUtcTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(routeWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == routeWorkflow && row.OperationalStateIdOrderKey.CompareTo(routeOperational) > 0));
            }
            else if (isLeaseAcquiredRoute)
            {
                source = source.Where(row =>
                    row.LeaseAcquiredAtUtcTicks > routeCursor.SignalUtcTicks ||
                    row.LeaseAcquiredAtUtcTicks == routeCursor.SignalUtcTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(routeWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == routeWorkflow && row.OperationalStateIdOrderKey.CompareTo(routeOperational) > 0));
            }
            else
            {
                source = source.Where(row =>
                    row.HeartbeatRecordedAtUtcTicks > routeCursor.SignalUtcTicks ||
                    row.HeartbeatRecordedAtUtcTicks == routeCursor.SignalUtcTicks &&
                    (row.WorkflowExecutionIdOrderKey.CompareTo(routeWorkflow) > 0 ||
                     row.WorkflowExecutionIdOrderKey == routeWorkflow && row.OperationalStateIdOrderKey.CompareTo(routeOperational) > 0));
            }
        }

        IOrderedQueryable<ExecutionLivenessStateEntity> ordered;
        if (owner is null)
        {
            ordered = route switch
            {
                0 => source.OrderBy(row => row.InterruptedAtUtcTicks),
                1 => source.OrderBy(row => row.LeaseExpiresAtUtcTicks),
                2 => source.OrderBy(row => row.LeaseAcquiredAtUtcTicks),
                3 => source.OrderBy(row => row.HeartbeatRecordedAtUtcTicks),
                _ => throw new ArgumentOutOfRangeException(nameof(route))
            };
        }
        else
        {
            ordered = route switch
            {
                0 or 1 or 2 => source.OrderBy(row => row.InterruptedAtUtcTicks),
                3 => source.OrderBy(row => row.LeaseExpiresAtUtcTicks),
                4 => source.OrderBy(row => row.LeaseAcquiredAtUtcTicks),
                5 => source.OrderBy(row => row.HeartbeatRecordedAtUtcTicks),
                _ => throw new ArgumentOutOfRangeException(nameof(route))
            };
        }
        return await ordered.ThenBy(row => row.WorkflowExecutionIdOrderKey).ThenBy(row => row.OperationalStateIdOrderKey)
            .Take(checked(limit + 1)).ToArrayAsync(cancellationToken);
    }

    private static RoutePageCursor CreateRouteCursor(ExecutionLivenessStateEntity row, int route, bool ownerFiltered)
    {
        var isInterruptionRoute = ownerFiltered ? route <= 2 : route == 0;
        var isLeaseExpiryRoute = ownerFiltered ? route == 3 : route == 1;
        var isLeaseAcquiredRoute = ownerFiltered ? route == 4 : route == 2;
        var signal = isInterruptionRoute
            ? row.InterruptedAtUtcTicks
            : isLeaseExpiryRoute
                ? row.LeaseExpiresAtUtcTicks
                : isLeaseAcquiredRoute
                    ? row.LeaseAcquiredAtUtcTicks
                    : row.HeartbeatRecordedAtUtcTicks;
        if (signal is null)
            throw new InvalidDataException("The recovery route returned a row without its due-time projection.");
        return new RoutePageCursor(signal.Value, row.WorkflowExecutionId, row.OperationalStateId);
    }

    private async Task<ExecutionLivenessStateEntity?> LoadAsync(string scope, string workflowExecutionId, string operationalStateId, bool tracking, CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, operationalStateId);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var workflow = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var opHash = EfRuntimeOperationalStoreSupport.Hash(operationalStateId);
        var op = EfRuntimeOperationalStoreSupport.Encode(operationalStateId);
        var query = context.ExecutionLivenessStates.Where(row => row.Id == id && row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey && row.WorkflowExecutionIdHash == workflowHash && row.WorkflowExecutionId == workflow && row.OperationalStateIdHash == opHash && row.OperationalStateId == op);
        return tracking ? await query.SingleOrDefaultAsync(cancellationToken) : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    internal static ExecutionLivenessStateEntity ToEntity(ExecutionLivenessState state, string scope, long revision)
    {
        var lease = state.ExecutionLease;
        var heartbeat = state.Heartbeat;
        return new ExecutionLivenessStateEntity
        {
            Id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId),
            ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
            WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId), WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId), WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId),
            OperationalStateId = EfRuntimeOperationalStoreSupport.Encode(state.ExecutionLivenessStateId), OperationalStateIdHash = EfRuntimeOperationalStoreSupport.Hash(state.ExecutionLivenessStateId), OperationalStateIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.ExecutionLivenessStateId),
            InterruptedStatus = state.InterruptedExecution is { } interrupted ? (int)interrupted.Status : null,
            InterruptedAtUtcTicks = state.InterruptedExecution?.InterruptedAt.UtcTicks,
            LeaseOwnerId = lease is null ? null : EfRuntimeOperationalStoreSupport.Encode(lease.OwnerId),
            LeaseAcquiredAtUtcTicks = lease?.AcquiredAt.UtcTicks,
            LeaseExpiresAtUtcTicks = lease?.ExpiresAt.UtcTicks,
            HeartbeatOwnerId = heartbeat is null ? null : EfRuntimeOperationalStoreSupport.Encode(heartbeat.OwnerId),
            HeartbeatRecordedAtUtcTicks = heartbeat?.RecordedAt.UtcTicks,
            HasOperationalOwner = lease is not null || heartbeat is not null,
            ContentJson = RuntimeArtifactJson.Serialize(state), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = revision
        };
    }

    internal static void Copy(ExecutionLivenessStateEntity row, ExecutionLivenessState state, string scope, long revision)
    {
        var replacement = ToEntity(state, scope, revision);
        row.Id = replacement.Id; row.ScopeKey = replacement.ScopeKey; row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.WorkflowExecutionId = replacement.WorkflowExecutionId; row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash; row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey;
        row.OperationalStateId = replacement.OperationalStateId; row.OperationalStateIdHash = replacement.OperationalStateIdHash; row.OperationalStateIdOrderKey = replacement.OperationalStateIdOrderKey;
        row.InterruptedStatus = replacement.InterruptedStatus; row.InterruptedAtUtcTicks = replacement.InterruptedAtUtcTicks;
        row.LeaseOwnerId = replacement.LeaseOwnerId; row.LeaseAcquiredAtUtcTicks = replacement.LeaseAcquiredAtUtcTicks; row.LeaseExpiresAtUtcTicks = replacement.LeaseExpiresAtUtcTicks;
        row.HeartbeatOwnerId = replacement.HeartbeatOwnerId; row.HeartbeatRecordedAtUtcTicks = replacement.HeartbeatRecordedAtUtcTicks; row.HasOperationalOwner = replacement.HasOperationalOwner;
        row.ContentJson = replacement.ContentJson; row.SchemaVersion = replacement.SchemaVersion; row.Revision = revision;
    }

    // Internal checkpoint staging uses the same envelope/projection validation before touching the ownership fence.
    internal static ExecutionLivenessState Read(ExecutionLivenessStateEntity row, string scope, string? expectedWorkflow = null, string? expectedOperational = null)
    {
        if (row.Revision <= 0 || row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) || row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope))
            throw new InvalidDataException("The execution-liveness row scope or revision projection is corrupt.");
        ExecutionLivenessState state;
        try { state = RuntimeArtifactJson.Deserialize<ExecutionLivenessState>(row.ContentJson); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("The persisted execution-liveness state is not valid current data.", exception); }
        var lease = state.ExecutionLease;
        var heartbeat = state.Heartbeat;
        var valid = EfSchemaVersion.Readable("RuntimeOperationalState", row.SchemaVersion, RuntimeOperationalStateEfModule.SchemaVersion) &&
                    (expectedWorkflow is null || state.WorkflowExecutionId == expectedWorkflow) &&
                    (expectedOperational is null || state.ExecutionLivenessStateId == expectedOperational) &&
                    row.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId) &&
                    row.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId) && row.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) && row.WorkflowExecutionIdOrderKey == EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId) &&
                    row.OperationalStateId == EfRuntimeOperationalStoreSupport.Encode(state.ExecutionLivenessStateId) && row.OperationalStateIdHash == EfRuntimeOperationalStoreSupport.Hash(state.ExecutionLivenessStateId) && row.OperationalStateIdOrderKey == EfRuntimeOperationalStoreSupport.Order(state.ExecutionLivenessStateId) &&
                    row.InterruptedStatus == (state.InterruptedExecution is { } interrupted ? (int)interrupted.Status : null) && row.InterruptedAtUtcTicks == state.InterruptedExecution?.InterruptedAt.UtcTicks &&
                    row.LeaseOwnerId == (lease is null ? null : EfRuntimeOperationalStoreSupport.Encode(lease.OwnerId)) && row.LeaseAcquiredAtUtcTicks == lease?.AcquiredAt.UtcTicks && row.LeaseExpiresAtUtcTicks == lease?.ExpiresAt.UtcTicks &&
                    row.HeartbeatOwnerId == (heartbeat is null ? null : EfRuntimeOperationalStoreSupport.Encode(heartbeat.OwnerId)) && row.HeartbeatRecordedAtUtcTicks == heartbeat?.RecordedAt.UtcTicks && row.HasOperationalOwner == (lease is not null || heartbeat is not null);
        if (!valid)
            throw new InvalidDataException("The execution-liveness row identity or recovery projection is corrupt.");
        return state;
    }

    private static void ValidateState(ExecutionLivenessState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId));
        ValidateIdentity(state.ExecutionLivenessStateId, nameof(state.ExecutionLivenessStateId));
        if (state.ExecutionLease is { } lease) ValidateIdentity(lease.OwnerId, nameof(lease.OwnerId));
        if (state.Heartbeat is { } heartbeat) ValidateIdentity(heartbeat.OwnerId, nameof(heartbeat.OwnerId));
    }

    private static void ValidateIdentityPair(string workflowExecutionId, string operationalStateId)
    {
        ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        ValidateIdentity(operationalStateId, nameof(operationalStateId));
    }

    private static void ValidateIdentity(string value, string parameterName) =>
        EfRuntimeOperationalStoreSupport.ValidateIdentity(value, parameterName);

    private static string Identity(string workflowExecutionId, string operationalStateId) => $"{workflowExecutionId.Length}:{workflowExecutionId}{operationalStateId.Length}:{operationalStateId}";

    private string EncodeIdentityCursor(string scope, string workflow, string last, string purpose)
    {
        var token = continuationCodec.Encode(purpose, JsonSerializer.SerializeToUtf8Bytes(new IdentityCursor(scope, workflow, last)));
        return RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token))!;
    }

    private IdentityCursor? DecodeIdentityCursor(string? token, string purpose, string scope, string workflow, RuntimeStorePageRequest request)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<IdentityCursor>(continuationCodec.Decode(purpose, token)) ?? throw new InvalidDataException();
            if (cursor.Scope != scope || cursor.WorkflowExecutionId != workflow) throw new InvalidDataException("The continuation belongs to another query.");
            EfRuntimeOperationalStoreSupport.ValidateIdentity(cursor.Last, nameof(request));
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        { throw new ArgumentException("The execution-liveness continuation is invalid or belongs to another query.", nameof(request), exception); }
    }

    private string EncodeGlobalCursor(string scope, ExecutionLivenessState state)
    {
        var token = continuationCodec.Encode(GlobalCursorPurpose, JsonSerializer.SerializeToUtf8Bytes(new GlobalCursor(scope, state.WorkflowExecutionId, state.ExecutionLivenessStateId)));
        return RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token))!;
    }

    private GlobalCursor? DecodeGlobalCursor(string? token, string scope, RuntimeStorePageRequest request)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<GlobalCursor>(continuationCodec.Decode(GlobalCursorPurpose, token)) ?? throw new InvalidDataException();
            if (cursor.Scope != scope) throw new InvalidDataException("The continuation belongs to another persistence scope.");
            EfRuntimeOperationalStoreSupport.ValidateIdentity(cursor.WorkflowExecutionId, nameof(request));
            EfRuntimeOperationalStoreSupport.ValidateIdentity(cursor.OperationalStateId, nameof(request));
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        { throw new ArgumentException("The execution-liveness continuation is invalid or belongs to another query.", nameof(request), exception); }
    }

    private static string RecoveryBinding(string scope, RuntimeRecoveryScanRequest request) =>
        JsonSerializer.Serialize(new RecoveryScanBinding(
            scope,
            request.Now.UtcTicks,
            request.LeaseTimeout.Ticks,
            request.HeartbeatTimeout.Ticks,
            request.OwnerId));

    private string EncodeRecoveryCursor(string binding, DateTimeOffset eligibleAt, ExecutionLivenessState state)
    {
        var token = continuationCodec.Encode(RecoveryCursorPurpose, JsonSerializer.SerializeToUtf8Bytes(new RecoveryCursor(binding, eligibleAt.UtcTicks, state.WorkflowExecutionId, state.ExecutionLivenessStateId)));
        return RuntimeStorePageRequest.ValidateContinuationToken(token, nameof(token))!;
    }

    private RecoveryCursor? DecodeRecoveryCursor(string? token, string binding, RuntimeStorePageRequest request)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<RecoveryCursor>(continuationCodec.Decode(RecoveryCursorPurpose, token)) ?? throw new InvalidDataException();
            if (cursor.Binding != binding) throw new InvalidDataException("The continuation belongs to another recovery scan.");
            EfRuntimeOperationalStoreSupport.ValidateIdentity(cursor.WorkflowExecutionId, nameof(request));
            EfRuntimeOperationalStoreSupport.ValidateIdentity(cursor.OperationalStateId, nameof(request));
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        { throw new ArgumentException("The execution-liveness recovery continuation is invalid or belongs to another scan.", nameof(request), exception); }
    }

    private static int Compare(DateTimeOffset eligibleAt, ExecutionLivenessState state, RecoveryCursor cursor)
    {
        var result = eligibleAt.UtcTicks.CompareTo(cursor.EligibleAtUtcTicks);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
        return result != 0 ? result : StringComparer.Ordinal.Compare(state.ExecutionLivenessStateId, cursor.OperationalStateId);
    }

    private sealed record IdentityCursor(string Scope, string WorkflowExecutionId, string Last);
    private sealed record GlobalCursor(string Scope, string WorkflowExecutionId, string OperationalStateId);
    private sealed record RecoveryScanBinding(string Scope, long NowUtcTicks, long LeaseTimeoutTicks, long HeartbeatTimeoutTicks, string? OwnerId);
    private sealed record RecoveryCursor(string Binding, long EligibleAtUtcTicks, string WorkflowExecutionId, string OperationalStateId)
    {
        public DateTimeOffset EligibleAt => new(EligibleAtUtcTicks, TimeSpan.Zero);
    }
    private sealed record RoutePageCursor(long SignalUtcTicks, string WorkflowExecutionId, string OperationalStateId);
}
