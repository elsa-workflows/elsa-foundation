using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide state shared by scoped in-memory checkpoint-store adapters.</summary>
public sealed class InMemoryRuntimeCheckpointStoreState
{
    internal object SyncRoot { get; } = new();
    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal Dictionary<string, RuntimeCheckpointCommitRecord> Commits { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuntimePostCommitOutboxItem> OutboxItems { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowDispatchRecord> WorkflowDispatches { get; } = new(StringComparer.Ordinal);
}

public sealed class InMemoryRuntimeCheckpointCommitStore : IRuntimeCheckpointCommitStore, IRuntimePostCommitOutboxStore, IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly IActivityExecutionInspectionWriter? _activityExecutionInspectionWriter;
    private readonly IBookmarkStateStore? _bookmarkStateStore;
    private readonly IDurableValueStateStore? _durableValueStateStore;
    private readonly IIncidentStateStore? _incidentStateStore;
    private readonly IExecutionLivenessStateStore? _operationalStateStore;
    private readonly ISchedulerStateStore? _schedulerStateStore;
    private readonly IActivityScopeCleanupStore? _activityScopeCleanupStore;
    private readonly IActivityExecutionHierarchyWriter? _activityExecutionHierarchyWriter;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly IWorkflowExecutableRootWriteLeaseManager? _rootWriteLeaseManager;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the in-memory commit store. RT-8: the seven telescoping constructors collapsed into this single primary
    /// constructor with every backing store optional. All parameters default to <c>null</c> so both the terse test
    /// constructions and the DI activation (which injects every registered backing store, unchanged) resolve to the same
    /// shape — W9's coalescing decorators keep wrapping this registration without modification.
    /// </summary>
    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager,
        InMemoryRuntimeCheckpointStoreState? state,
        TimeProvider? timeProvider)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager,
            state,
            timeProvider,
            workflowDispatchStore: null)
    {
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IBookmarkStateStore? bookmarkStateStore = null,
        IDurableValueStateStore? durableValueStateStore = null,
        IIncidentStateStore? incidentStateStore = null,
        IExecutionLivenessStateStore? operationalStateStore = null,
        ISchedulerStateStore? schedulerStateStore = null,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter = null,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null,
        InMemoryRuntimeCheckpointStoreState? state = null,
        TimeProvider? timeProvider = null,
        IActivityScopeCleanupStore? activityScopeCleanupStore = null,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter = null,
        IWorkflowDispatchStore? workflowDispatchStore = null)
    {
        _state = state ?? new InMemoryRuntimeCheckpointStoreState();
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _activityExecutionInspectionWriter = activityExecutionInspectionWriter;
        _bookmarkStateStore = bookmarkStateStore;
        _durableValueStateStore = durableValueStateStore;
        _incidentStateStore = incidentStateStore;
        _operationalStateStore = operationalStateStore;
        _schedulerStateStore = schedulerStateStore;
        _activityScopeCleanupStore = activityScopeCleanupStore;
        _activityExecutionHierarchyWriter = activityExecutionHierarchyWriter;
        _rootWriteLeaseManager = rootWriteLeaseManager;
        _workflowDispatchStore = workflowDispatchStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager,
        InMemoryRuntimeCheckpointStoreState? state,
        TimeProvider? timeProvider,
        IActivityScopeCleanupStore? activityScopeCleanupStore,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager,
            state,
            timeProvider,
            activityScopeCleanupStore,
            activityExecutionHierarchyWriter,
            workflowDispatchStore: null)
    {
    }

    public InMemoryRuntimeCheckpointCommitStore(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IExecutionLivenessStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter,
        IActivityScopeCleanupStore? activityScopeCleanupStore,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter)
        : this(
            workflowExecutionStateStore,
            activityExecutionStateStore,
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            schedulerStateStore,
            activityExecutionInspectionWriter,
            rootWriteLeaseManager: null,
            state: null,
            timeProvider: null,
            activityScopeCleanupStore: activityScopeCleanupStore,
            activityExecutionHierarchyWriter: activityExecutionHierarchyWriter)
    {
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
            lock (_state.SyncRoot)
            {
                if (_state.Commits.TryGetValue(commit.CommitId, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(
                            RuntimeCheckpointCommitFingerprint.Compute(existing.Commit),
                            fingerprint))
                    {
                        throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
                    }
                    return new RuntimeCheckpointCommitStoreResult(existing.PendingPostCommitWorkIds);
                }
            }

            if (commit.ExpectedFence is null)
                return await CommitNewAsync(commit, decision, cancellationToken);

            if (_operationalStateStore is not InMemoryExecutionLivenessStateStore inMemoryOperationalStateStore)
            {
                throw new InvalidOperationException(
                    "A fenced in-memory checkpoint requires the in-memory execution liveness-state store so ownership validation and checkpoint writes share one atomic boundary.");
            }

            return await inMemoryOperationalStateStore.ExecuteOwnershipAtomicAsync(
                ct => CommitNewAsync(commit, decision, ct),
                cancellationToken);
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        await EnsureExpectedFenceAsync(commit, cancellationToken);

        var pendingOutboxItems = commit.StateChanges.PostCommitOutbox.Select(change => change.State).ToArray();
        ValidatePendingOutboxItems(pendingOutboxItems);
        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
        ValidateSchedulerStateChange(commit);
        ValidateActivityExecutionStateChanges(commit);
        ValidateActivityExecutionInspectionChanges(commit);
        ValidateBookmarkStateChanges(commit);
        ValidateDurableValueStateChanges(commit);
        ValidateIncidentStateChanges(commit);
        ValidateOperationalStateChanges(commit);
        await ValidateWorkflowDispatchChangesAsync(commit, cancellationToken);
        await ExecuteWithWorkflowExecutionRootWriteLeaseAsync(commit, async ct =>
        {
            await ApplyWorkflowExecutionStateChangeAsync(commit.StateChanges.WorkflowExecution, ct);
            await ApplySchedulerStateChangeAsync(commit.StateChanges.Scheduler, ct);
            await ApplyActivityExecutionStateChangesAsync(commit.StateChanges.ActivityExecutions, ct);
            await ApplyActivityExecutionInspectionChangesAsync(commit.StateChanges.ActivityExecutionInspections, ct);
            await ApplyActivityExecutionHierarchyChangesAsync(commit.StateChanges.ActivityExecutionInspections, ct);
            await ApplyBookmarkStateChangesAsync(commit.StateChanges.Bookmarks, ct);
            await ApplyDurableValueStateChangesAsync(commit.StateChanges.DurableValues, ct);
            await ApplyIncidentStateChangesAsync(commit.StateChanges.Incidents, ct);
            await ApplyOperationalStateChangesAsync(commit.StateChanges.Operational, ct);
            await ApplyActivityScopeCleanupsAsync(commit.StateChanges.ActivityScopeCleanups, ct);
            await ApplyWorkflowDispatchChangesAsync(commit.StateChanges.WorkflowDispatches, ct);

            try
            {
                // #386: all outbox validation is front-loaded in ValidatePendingOutboxItems (and commits
                // are serialized by the write gate), so an exception here is a genuine partial-persistence
                // risk — the projections above have been applied but the commit record/outbox may not be
                // durably recorded. Only that condition warrants the inconsistent-durability wrapper.
                lock (_state.SyncRoot)
                {
                    foreach (var item in pendingOutboxItems)
                        SavePendingOutboxItem(item);

                    _state.Commits.Add(commit.CommitId, new RuntimeCheckpointCommitRecord(commit, decision, pendingOutboxItems.Select(item => item.OutboxItemId).ToArray()));
                }
            }
            catch (Exception exception)
            {
                throw new RuntimeCheckpointInconsistentDurabilityException(commit.CommitId, exception);
            }
        }, cancellationToken);

        return new RuntimeCheckpointCommitStoreResult(pendingOutboxItems.Select(item => item.OutboxItemId).ToArray());
    }

    private async ValueTask EnsureExpectedFenceAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return;
        if (_operationalStateStore is null)
        {
            throw new InvalidOperationException(
                "A checkpoint carrying an execution fence requires an execution liveness-state store.");
        }

        var state = await _operationalStateStore.FindAsync(
            commit.WorkflowExecutionId,
            InMemoryExecutionLivenessStateStore.GetOwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        var currentToken = ReadHighestIssuedToken(state);
        var current = state?.ExecutionLease;
        var reason = current is null
            ? RuntimeFencingRejectionReason.NoActiveLease
            : current.IsExpired(_timeProvider.GetUtcNow())
                ? RuntimeFencingRejectionReason.ExpiredLease
                : RuntimeFencingRejectionReason.StaleToken;
        if (current is null ||
            current.IsExpired(_timeProvider.GetUtcNow()) ||
            !StringComparer.Ordinal.Equals(current.LeaseId, expectedFence.LeaseId) ||
            !StringComparer.Ordinal.Equals(current.OwnerId, expectedFence.OwnerId) ||
            current.FencingToken != expectedFence.FencingToken)
        {
            throw new RuntimeStaleFencingTokenException(
                commit.WorkflowExecutionId,
                expectedFence.FencingToken,
                currentToken,
                reason);
        }
    }

    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
        {
            return token;
        }

        return state.ExecutionLease?.FencingToken ?? 0;
    }

    private async ValueTask ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
        RuntimeCheckpointCommit commit,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is null || commit.StateChanges.WorkflowExecution is not { } workflowExecutionChange)
        {
            await write(cancellationToken);
            return;
        }

        if (_rootWriteLeaseManager is null)
            throw new InvalidOperationException("A workflow executable root-write lease manager is required before workflow execution state can be persisted.");

        await _rootWriteLeaseManager.ExecuteAsync(
            workflowExecutionChange.State.PinnedExecutable,
            $"checkpoint:{commit.CommitId}",
            write,
            cancellationToken);
    }

    public IReadOnlyCollection<RuntimeCheckpointCommitRecord> ListCommits()
    {
        lock (_state.SyncRoot)
        {
            return _state.Commits.Values.ToArray();
        }
    }

    /// <summary>Test seam: seeds a pending outbox item directly, bypassing the commit path (§2.23.3).</summary>
    public ValueTask AddPendingForTestingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            SavePendingOutboxItem(item);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.OwnerId is not null)
            throw new NotSupportedException("The in-memory post-commit outbox store does not implement delivery ownership filtering.");

        lock (_state.SyncRoot)
        {
            var items = _state.OutboxItems.Values
                .Where(item => IsDeliverable(item, query))
                .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(items);
        }
    }

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            return new ValueTask<RuntimePostCommitOutboxItem?>(
                _state.OutboxItems.TryGetValue(outboxItemId, out var item) ? item : null);
        }
    }

    public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(result.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found.");

            if (existing.IsTerminal)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");
            if (existing.Status == RuntimePostCommitOutboxStatus.Delivering)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");

            var deliveryAttemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(existing.DeliveryAttemptCount);
            var status = NormalizeDeliveryStatus(existing, result.Status, deliveryAttemptCount);
            DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
                ? NextRetryAvailableAt(existing, result.RecordedAt)
                : null;

            _state.OutboxItems[result.OutboxItemId] = new RuntimePostCommitOutboxItem(
                outboxItemId: existing.OutboxItemId,
                intent: existing.Intent,
                status: status,
                recordedAt: existing.RecordedAt,
                availableAt: availableAt,
                retryPolicy: existing.RetryPolicy,
                deliveryAttemptCount: deliveryAttemptCount,
                deliveringOwnerId: null,
                deliveryStartedAt: null,
                deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
                lastFailureMessage: result.FailureMessage,
                metadata: existing.Metadata,
                deliveryFencingToken: existing.DeliveryFencingToken);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var claimable = _state.OutboxItems.Values
                .Where(item => RuntimePostCommitOutboxClaimTransitions.CanClaim(item, request))
                .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(request.Limit)
                .ToArray();
            var claims = new RuntimePostCommitOutboxClaim[claimable.Length];
            for (var index = 0; index < claimable.Length; index++)
            {
                var claim = RuntimePostCommitOutboxClaimTransitions.Claim(claimable[index], request);
                _state.OutboxItems[claim.OutboxItemId] = claim.Item;
                claims[index] = claim;
            }

            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>(claims);
        }
    }

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(claim.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{claim.OutboxItemId}' was not found.");

            _state.OutboxItems[claim.OutboxItemId] = RuntimePostCommitOutboxClaimTransitions.Complete(existing, claim, result);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
                throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");

            var completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
                existingOutbox,
                completion.Claim,
                completion.DeliveryResult);
            if (completion.WorkflowDispatch is { } dispatch)
            {
                if (!_state.WorkflowDispatches.TryGetValue(dispatch.DispatchId, out var existingDispatch))
                    throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the atomic checkpoint store.");
                WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, dispatch);
            }

            _state.OutboxItems[completion.Claim.OutboxItemId] = completedOutbox;
            if (completion.WorkflowDispatch is { } workflowDispatch)
                _state.WorkflowDispatches[workflowDispatch.DispatchId] = workflowDispatch;
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        await _workflowExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    /// <summary>
    /// Validates every pending outbox item in the commit — status, conflicts against the store, and
    /// conflicts <b>within the commit itself</b> — before any state is projected or mutated (#386).
    /// This runs outside the inconsistent-durability guard so a data-conflict validation failure
    /// surfaces as a plain <see cref="InvalidOperationException"/> instead of being misclassified as
    /// a <see cref="RuntimeCheckpointInconsistentDurabilityException"/>: with all validation front-
    /// loaded here (and commits serialized by the write gate), nothing validation-shaped can throw
    /// inside the guarded mutation block.
    /// </summary>
    private void ValidatePendingOutboxItems(IReadOnlyCollection<RuntimePostCommitOutboxItem> items)
    {
        lock (_state.SyncRoot)
        {
            var seen = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (item.Status != RuntimePostCommitOutboxStatus.Pending)
                    throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

                if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing) && !IsSamePendingIntent(existing, item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");

                if (seen.TryGetValue(item.OutboxItemId, out var duplicate) && !IsSamePendingIntent(duplicate, item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");

                seen[item.OutboxItemId] = item;
            }
        }
    }

    private void SavePendingOutboxItem(RuntimePostCommitOutboxItem item)
    {
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing))
        {
            if (IsSamePendingIntent(existing, item))
                return;

            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
        }

        _state.OutboxItems.Add(item.OutboxItemId, item);
    }

    private async ValueTask ApplySchedulerStateChangeAsync(
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_schedulerStateStore is null || stateChange is null)
            return;

        await _schedulerStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
            await _activityExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionInspectionChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionInspectionWriter is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _activityExecutionInspectionWriter.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected activity execution inspection state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyActivityExecutionHierarchyChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionHierarchyWriter is null)
            return;
        foreach (var stateChange in stateChanges)
        {
            var projection = stateChange.State;
            if (!string.IsNullOrWhiteSpace(projection.ExecutionScopeId ?? projection.Provenance.ExecutionScopeId))
                await _activityExecutionHierarchyWriter.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection), cancellationToken);
        }
    }

    private async ValueTask ApplyBookmarkStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _bookmarkStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.BookmarkId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _bookmarkStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected bookmark state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyDurableValueStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _durableValueStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.DurableValueId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _durableValueStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected durable value state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyIncidentStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await _incidentStateStore.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    throw new InvalidOperationException($"Incident state '{stateChange.State.IncidentId}' already exists for workflow execution '{stateChange.State.WorkflowExecutionId}'.");

                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _incidentStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected incident state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyOperationalStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _operationalStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected operational state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyActivityScopeCleanupsAsync(
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups,
        CancellationToken cancellationToken)
    {
        if (_activityScopeCleanupStore is null)
        {
            if (cleanups.Count != 0)
                throw new InvalidOperationException("The checkpoint contains activity-scope cleanup but no cleanup store is configured.");
            return;
        }

        foreach (var cleanup in cleanups)
            await _activityScopeCleanupStore.ApplyAsync(cleanup, cancellationToken);
    }

    private async ValueTask ApplyWorkflowDispatchChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (stateChanges.Count == 0)
            return;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("Workflow dispatch checkpoint changes require an IWorkflowDispatchStore.");

        foreach (var stateChange in stateChanges)
            await _workflowDispatchStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint commit store can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (_schedulerStateStore is null || commit.StateChanges.Scheduler is null)
            return;

        var stateChange = commit.StateChanges.Scheduler;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint commit store can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");

        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
    }

    private void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateActivityExecutionInspectionChanges(RuntimeCheckpointCommit commit)
    {
        if (_activityExecutionInspectionWriter is null)
            return;

        foreach (var stateChange in commit.StateChanges.ActivityExecutionInspections)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project activity execution inspection '{RuntimeStateChangeOperation.Upsert}' changes.");

            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution inspection state change StateId must match ActivityExecutionInspectionProjection.ActivityExecutionId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution inspection WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateIncidentStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the commit store repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match ExecutionLivenessState.OperationalStateId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (StringComparer.Ordinal.Equals(
                    stateChange.State.OperationalStateId,
                    InMemoryExecutionLivenessStateStore.GetOwnershipStateId(commit.WorkflowExecutionId)))
            {
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
            }
        }
    }

    private async ValueTask ValidateWorkflowDispatchChangesAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowDispatches.Count == 0)
            return;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("Workflow dispatch checkpoint changes require an IWorkflowDispatchStore.");

        var seen = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var stateChange in commit.StateChanges.WorkflowDispatches)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint commit store can only project workflow dispatch '{RuntimeStateChangeOperation.Upsert}' changes.");
            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, stateChange.State);

            if (seen.TryGetValue(stateChange.StateId, out var duplicate) && !WorkflowDispatchLifecycle.RecordsEqual(duplicate, stateChange.State))
                throw new InvalidOperationException($"Workflow dispatch '{stateChange.StateId}' occurs more than once with conflicting state in the same checkpoint.");
            seen[stateChange.StateId] = stateChange.State;

            var existing = await _workflowDispatchStore.FindAsync(stateChange.StateId, cancellationToken);
            if (existing is not null)
                WorkflowDispatchLifecycle.ValidateTransition(existing, stateChange.State);
            else
                WorkflowDispatchLifecycle.ValidateNew(stateChange.State);
        }
    }

    private static bool IsSamePendingIntent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        existing.Status == RuntimePostCommitOutboxStatus.Pending
        && StringComparer.Ordinal.Equals(existing.Intent.IntentId, item.Intent.IntentId)
        && StringComparer.Ordinal.Equals(existing.Intent.WorkflowExecutionId, item.Intent.WorkflowExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.Kind, item.Intent.Kind)
        && StringComparer.Ordinal.Equals(existing.Intent.ActivityExecutionId, item.Intent.ActivityExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.IdempotencyKey, item.Intent.IdempotencyKey)
        && StringComparer.Ordinal.Equals(existing.Intent.DependsOnWaitRegistrationId, item.Intent.DependsOnWaitRegistrationId)
        && existing.Intent.WaitFailurePolicy == item.Intent.WaitFailurePolicy
        && PayloadEquals(existing.Intent.Payload, item.Intent.Payload)
        && MetadataEquals(existing.Intent.Metadata, item.Intent.Metadata);

    private static bool PayloadEquals(System.Text.Json.JsonElement? left, System.Text.Json.JsonElement? right)
    {
        if (left.HasValue != right.HasValue)
            return false;

        return !left.HasValue || StringComparer.Ordinal.Equals(left.Value.GetRawText(), right!.Value.GetRawText());
    }

    private static bool MetadataEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));
    }

    private static bool IsDeliverable(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        if (query.WorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, query.WorkflowExecutionId))
            return false;

        if (query.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, query.IntentKind))
            return false;

        if (item.AvailableAt is { } availableAt && availableAt > query.Now)
            return false;

        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;

        if (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
            return !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount);

        return false;
    }

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int deliveryAttemptCount)
    {
        if (status != RuntimePostCommitOutboxStatus.FailedRetryable)
            return status;

        return existing.RetryPolicy.IsExhaustedAfterAttempt(deliveryAttemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : RuntimePostCommitOutboxStatus.FailedRetryable;
    }

    private static DateTimeOffset NextRetryAvailableAt(RuntimePostCommitOutboxItem existing, DateTimeOffset recordedAt) =>
        existing.RetryPolicy.Delay is { } delay ? recordedAt.Add(delay) : recordedAt;
}

public sealed record RuntimeCheckpointCommitRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision,
    IReadOnlyCollection<string> PendingPostCommitWorkIds);
