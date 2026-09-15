using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
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
        if (!StringComparer.Ordinal.Equals(change.StateId, change.State.DurableValueId))
            throw new InvalidOperationException("Durable value state change StateId must match its model identity.");
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

            // Groundwork's conditional delete is idempotent for a missing row.
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
        var operationalStateId = $"ownership:{workflowExecutionId}";
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
