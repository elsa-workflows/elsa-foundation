using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Internal staging seam for the bounded R19 checkpoint slice.
/// </summary>
/// <remarks>
/// The direct R14-R18 stores own their public calls and therefore clear the change tracker around independent
/// writes. A checkpoint is different: all participants must remain tracked in one transaction. This seam reuses
/// their exact entity projections while deliberately exposing no domain or EF types outside the persistence
/// assembly.
/// </remarks>
internal static class EfRuntimeCheckpointParticipantStaging
{
    public static async ValueTask StageWorkflowExecutionAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<WorkflowExecutionState> change,
        string scope,
        DateTimeOffset occurredAt,
        Dictionary<string, WorkflowTestScope> touchedTestScopes,
        CancellationToken cancellationToken)
    {
        var id = EfWorkflowExecutionStateStore.CreateId(scope, change.StateId);
        var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(change.StateId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(change.StateId),
            cancellationToken);

        if (row is null)
        {
            if (change.State.TestScope is { } testScope)
                await EfRuntimeCheckpointTestScopeParticipantStaging.AssertOpenAndStageAsync(
                    context, testScope, change.State.WorkflowExecutionId, occurredAt,
                    scope, touchedTestScopes, cancellationToken);
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(
                change.State,
                scope,
                id,
                1));
            return;
        }

        _ = EfWorkflowExecutionStateStore.ReadChecked(row, scope, change.StateId);
        EfWorkflowExecutionStateStore.CopyToEntity(row, change.State, scope, id, checked(row.Revision + 1));
    }

    public static async ValueTask StageSchedulerAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<SchedulerState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, change.StateId);
        var row = await context.SchedulerStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(change.StateId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(change.StateId),
            cancellationToken);

        if (row is null)
        {
            context.SchedulerStates.Add(EfSchedulerStateStore.ToEntity(
                change.State,
                scope,
                id,
                1));
            return;
        }

        _ = EfSchedulerStateStore.Read(row, scope, change.StateId);
        EfSchedulerStateStore.Copy(row, change.State, scope, checked(row.Revision + 1));
    }

    /// <summary>
    /// Stages one bookmark state change inside the caller-owned transaction.
    /// </summary>
    /// <remarks>
    /// The public bookmark store owns independent writes and clears the tracker around them. Checkpoint writes must
    /// retain all participants in one unit of work, so this seam only changes EF tracking state. SaveChanges,
    /// transaction creation, and commit remain the caller's responsibility.
    /// </remarks>
    public static async ValueTask StageBookmarkAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<BookmarkState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(change.State);
        if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
            throw new InvalidOperationException($"The EF checkpoint writer can only project bookmark '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
        EfBookmarkStateStore.ValidateState(change.State);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Bookmark state must be staged inside a caller-owned EF transaction.");

        var state = change.State;
        var id = EfBookmarkStateStore.CreateId(scope, state.WorkflowExecutionId, state.BookmarkId);
        // Load by immutable physical identity first. Filtering on projections would turn a corrupt row into a false
        // insert/miss instead of allowing the authoritative content and projection checks to fail closed.
        var row = await context.Bookmarks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
        {
            if (change.Operation == RuntimeStateChangeOperation.Upsert)
                context.Bookmarks.Add(EfBookmarkStateStore.ToEntity(state, scope, id, EfBookmarkStateStore.NewRevision()));

            // A conditional delete is idempotent for a missing row.
            return;
        }

        _ = EfBookmarkStateStore.MapChecked(row, scope, state.WorkflowExecutionId, state.BookmarkId, id);
        if (change.Operation == RuntimeStateChangeOperation.Delete)
        {
            context.Bookmarks.Remove(row);
            return;
        }

        EfBookmarkStateStore.CopyToEntity(row, state, scope, id, checked(row.Revision + 1));
    }

    /// <summary>
    /// Stages one durable-value state change inside the caller-owned transaction.
    /// </summary>
    /// <remarks>
    /// The public durable-value store owns independent writes and therefore clears the tracker around them. A
    /// checkpoint must retain all participants in one unit of work, so this seam deliberately only changes EF
    /// tracking state. SaveChanges, transaction creation, and commit remain the caller's responsibility.
    /// </remarks>
    public static async ValueTask StageDurableValueAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<DurableValueState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));
        ArgumentNullException.ThrowIfNull(change.State);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(change.State.WorkflowExecutionId, nameof(change.State.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(change.State.DurableValueId, nameof(change.State.DurableValueId));
        if (change.Operation is not (RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
            throw new InvalidOperationException($"The EF checkpoint writer can only project durable value '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Durable value state must be staged inside a caller-owned EF transaction.");

        var state = change.State;
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.DurableValueId);
        // Load by the immutable physical identity first. Filtering on projected fields would turn a corrupt row
        // into a false insert/miss instead of letting the authoritative content/projection validation fail closed.
        var row = await context.DurableValueStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
        {
            if (change.Operation == RuntimeStateChangeOperation.Upsert)
                context.DurableValueStates.Add(EfDurableValueStateStore.ToEntity(state, scope, id, 1));

            // A conditional delete is idempotent for a missing row.
            return;
        }

        _ = EfDurableValueStateStore.Read(row, scope, state.WorkflowExecutionId, state.DurableValueId);
        if (change.Operation == RuntimeStateChangeOperation.Delete)
        {
            context.DurableValueStates.Remove(row);
            return;
        }

        EfDurableValueStateStore.Copy(row, state, scope, checked(row.Revision + 1));
    }

    /// <summary>Stages execution-liveness state without committing or clearing sibling checkpoint writes.</summary>
    public static async ValueTask StageOperationalAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<ExecutionLivenessState> change,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(change.State.WorkflowExecutionId, nameof(change.State.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(change.State.OperationalStateId, nameof(change.State.OperationalStateId));
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Operational checkpoint changes require a caller-owned EF transaction.");

        var state = change.State;
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.OperationalStateId);
        // Read by immutable physical identity, then validate every projection; filtering a corrupt projection here
        // would turn a drifted persisted row into an incorrect insert or silent delete miss.
        var row = await context.ExecutionLivenessStates.SingleOrDefaultAsync(candidate => candidate.Id == id,
            cancellationToken);
        if (row is null)
        {
            if (change.Operation != RuntimeStateChangeOperation.Delete)
                context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(state, scope, 1));
            return;
        }

        _ = EfExecutionLivenessStateStore.Read(row, scope, state.WorkflowExecutionId, state.OperationalStateId);
        switch (change.Operation)
        {
            case RuntimeStateChangeOperation.Delete:
                context.ExecutionLivenessStates.Remove(row);
                break;
            case RuntimeStateChangeOperation.Append:
                throw new InvalidOperationException($"Operational state '{state.OperationalStateId}' already exists for create-only append.");
            case RuntimeStateChangeOperation.Upsert:
                EfExecutionLivenessStateStore.Copy(row, state, scope, checked(row.Revision + 1));
                break;
            default:
                throw new InvalidOperationException($"Unsupported operational state change '{change.Operation}'.");
        }
    }

    /// <summary>
    /// Consumes one claimed scheduler-work item inside the caller-owned transaction.
    /// </summary>
    /// <remarks>
    /// This is deliberately a direct EF bulk delete rather than a tracked remove. Renewal advances the provider
    /// revision while preserving the owner/token fence, so revision must not participate in consumption. The
    /// owner/token/workflow/scope predicate is the atomic fence; a zero-row result is the exact claim-conflict
    /// outcome. The source query is no-tracking and ExecuteDelete does not mutate the change tracker, allowing the
    /// caller to stage sibling participants and the immutable checkpoint marker on the same context safely.
    /// </remarks>
    public static async ValueTask StageConsumedSchedulerWorkAsync(
        BookmarkStateDbContext context,
        ConsumedSchedulerWorkItem consumed,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(consumed);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(consumed.WorkflowExecutionId, nameof(consumed.WorkflowExecutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(consumed.WorkItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumed.ClaimOwnerId);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Consumed scheduler work must be staged inside a caller-owned EF transaction.");

        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, consumed.WorkflowExecutionId, consumed.WorkItemId);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(consumed.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(consumed.WorkflowExecutionId);
        var workItemKey = EfRuntimeOperationalStoreSupport.Encode(consumed.WorkItemId);
        var workItemHash = EfRuntimeOperationalStoreSupport.Hash(consumed.WorkItemId);
        var ownerKey = EfRuntimeOperationalStoreSupport.Encode(consumed.ClaimOwnerId);

        var existing = await context.SchedulerWorkItems.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == id &&
            row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
            row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash &&
            row.WorkItemId == workItemKey && row.WorkItemIdHash == workItemHash,
            cancellationToken);
        if (existing is null)
            throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);

        _ = EfSchedulerWorkQueueStore.ReadChecked(
            existing,
            scope,
            consumed.WorkflowExecutionId,
            consumed.WorkItemId);
        if (existing.ClaimOwnerId is null ||
            !StringComparer.Ordinal.Equals(existing.ClaimOwnerId, ownerKey) ||
            existing.ClaimToken != consumed.FencingToken)
        {
            throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
        }

        // Do not include Revision: an in-flight renewal is allowed to replay the same owner/token consume. A
        // successor reclaim changes ClaimToken and is rejected by this atomic predicate.
        var deleted = await context.SchedulerWorkItems
            .Where(row =>
                row.Id == id &&
                row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
                row.WorkflowExecutionId == workflowKey && row.WorkflowExecutionIdHash == workflowHash &&
                row.WorkItemId == workItemKey && row.WorkItemIdHash == workItemHash &&
                row.ClaimOwnerId == ownerKey && row.ClaimToken == consumed.FencingToken)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted != 1)
            throw new RuntimeSchedulerWorkConsumeConflictException(consumed.WorkflowExecutionId, consumed.WorkItemId);
    }

    public static async ValueTask StageExecutionFenceAsync(
        BookmarkStateDbContext context,
        string scope,
        string workflowExecutionId,
        RuntimeExecutionFence expected,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var operationalStateId = RuntimeExecutionOwnershipStateId.For(workflowExecutionId);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, operationalStateId);
        var row = await context.ExecutionLivenessStates.SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            candidate.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            candidate.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId) &&
            candidate.OperationalStateIdHash == EfRuntimeOperationalStoreSupport.Hash(operationalStateId) &&
            candidate.OperationalStateId == EfRuntimeOperationalStoreSupport.Encode(operationalStateId),
            cancellationToken);

        if (row is null)
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                0,
                RuntimeFencingRejectionReason.NoActiveLease);

        var state = EfExecutionLivenessStateStore.Read(row, scope, workflowExecutionId, operationalStateId);
        var lease = state.ExecutionLease;
        var currentToken = lease?.FencingToken ?? ReadHighestIssuedToken(state);
        if (lease is null)
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.NoActiveLease);
        if (lease.IsExpired(timeProvider.GetUtcNow()))
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.ExpiredLease);
        if (!StringComparer.Ordinal.Equals(lease.LeaseId, expected.LeaseId) ||
            !StringComparer.Ordinal.Equals(lease.OwnerId, expected.OwnerId) ||
            lease.FencingToken != expected.FencingToken)
        {
            throw new RuntimeStaleFencingTokenException(
                workflowExecutionId,
                expected.FencingToken,
                currentToken,
                RuntimeFencingRejectionReason.StaleToken);
        }

        // Revision is the provider-neutral optimistic fence. Touching it in the same transaction makes a concurrent
        // lease transition fail the whole checkpoint instead of allowing state and marker rows to commit together.
        row.Revision = checked(row.Revision + 1);
    }

    private static long ReadHighestIssuedToken(ExecutionLivenessState state)
    {
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var token))
            return token;
        return state.ExecutionLease?.FencingToken ?? 0;
    }
}
