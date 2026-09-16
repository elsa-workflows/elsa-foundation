using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Application-wide state shared by scoped in-memory checkpoint-store adapters.</summary>
public sealed class InMemoryRuntimeCheckpointStoreState
{
    internal object SyncRoot { get; } = new();
    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal Dictionary<string, RuntimeCheckpointCommitRecord> Commits { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuntimePostCommitOutboxItem> OutboxItems { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowDispatchRecord> WorkflowDispatches { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowTestScopeRecord> WorkflowTestScopes { get; } = new(StringComparer.Ordinal);
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
    private readonly IWorkflowSchedulerWorkQueue? _schedulerWorkQueue;
    private readonly IWorkflowExecutableRootWriteLeaseManager? _rootWriteLeaseManager;
    private readonly IWorkflowAlterationStore? _alterationStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the in-memory commit store. Every backing store is optional and defaults to <c>null</c>, so terse test
    /// constructions and the DI activation (which injects every registered backing store) share this one constructor,
    /// and the coalescing decorators keep wrapping this registration without modification.
    /// </summary>
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
        IWorkflowDispatchStore? workflowDispatchStore = null,
        IWorkflowSchedulerWorkQueue? schedulerWorkQueue = null,
        IWorkflowAlterationStore? alterationStore = null)
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
        _schedulerWorkQueue = schedulerWorkQueue;
        _alterationStore = alterationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        // Reserved-key integrity comes before any gate: a fenced commit holds the ownership gate while it applies, and a
        // write to the ownership record would wait on that same gate forever instead of failing.
        RuntimeExecutionOwnershipStateId.EnsureNotWritten(commit.StateChanges.Operational);

        if (commit.StateChanges.AlterationJobTerminalChange is { } terminal &&
            _alterationStore is InMemoryWorkflowAlterationStore inMemoryAlterations)
        {
            RuntimeCheckpointCommitStoreResult? result = null;
            await inMemoryAlterations.CommitTerminalJobChangeAtomicallyAsync(terminal, async ct =>
                result = await CommitCoreAsync(commit, decision, ct), cancellationToken);
            return result ?? throw new InvalidOperationException("The alteration checkpoint gate did not produce a checkpoint result.");
        }

        return await CommitCoreAsync(commit, decision, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitCoreAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken)
    {

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
                    return new RuntimeCheckpointCommitStoreResult(existing.PendingPostCommitWorkIds)
                    {
                        ConsumedSchedulerWorkItemIds = existing.ConsumedSchedulerWorkItemIds
                    };
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
        var consumedSchedulerWorkItemIds = commit.StateChanges.ConsumedSchedulerWorkItems
            .Select(item => item.WorkItemId)
            .ToArray();
        // The commit's structural rules were applied by RuntimeCheckpointCommitValidator before it reached this store.
        // What remains here depends on the state this store currently holds.
        ValidatePendingOutboxItemsAgainstStore(pendingOutboxItems);
        await ValidateWorkflowTestScopesAsync(commit, cancellationToken);
        await ValidateIncidentChangesAgainstStoreAsync(commit, cancellationToken);
        await ValidateWorkflowDispatchChangesAsync(commit, cancellationToken);
        await ValidateWorkflowDispatchCancellationsAsync(commit, cancellationToken);
        await ValidateAlterationJobTerminalChangeAsync(commit, cancellationToken);
        await ExecuteWithWorkflowExecutionRootWriteLeaseAsync(commit, async ct =>
        {
            // Fence-checked consume first: a claim-lost outcome throws before any other state is mutated, so a stale
            // claimant's commit persists nothing (spec 105).
            await ApplyConsumedSchedulerWorkItemsAsync(commit.StateChanges.ConsumedSchedulerWorkItems, ct);
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
            await ApplyWorkflowDispatchCancellationsAsync(commit.StateChanges.WorkflowDispatchCancellations, ct);
            await ApplyAlterationJobTerminalChangeAsync(commit, ct);

            try
            {
                // #386: all outbox validation is front-loaded in ValidatePendingOutboxItemsAgainstStore (and commits
                // are serialized by the write gate), so an exception here is a genuine partial-persistence
                // risk — the projections above have been applied but the commit record/outbox may not be
                // durably recorded. Only that condition warrants the inconsistent-durability wrapper.
                lock (_state.SyncRoot)
                {
                    foreach (var item in pendingOutboxItems)
                        SavePendingOutboxItem(item);

                    _state.Commits.Add(commit.CommitId, new RuntimeCheckpointCommitRecord(
                        commit,
                        decision,
                        pendingOutboxItems.Select(item => item.OutboxItemId).ToArray(),
                        consumedSchedulerWorkItemIds));
                }
            }
            catch (Exception exception) when (exception is not RuntimeSchedulerWorkConsumeConflictException)
            {
                throw new RuntimeCheckpointInconsistentDurabilityException(commit.CommitId, exception);
            }
        }, cancellationToken);

        return new RuntimeCheckpointCommitStoreResult(pendingOutboxItems.Select(item => item.OutboxItemId).ToArray())
        {
            ConsumedSchedulerWorkItemIds = consumedSchedulerWorkItemIds
        };
    }

    private async ValueTask ApplyConsumedSchedulerWorkItemsAsync(
        IReadOnlyCollection<ConsumedSchedulerWorkItem> consumed,
        CancellationToken cancellationToken)
    {
        if (consumed.Count == 0)
            return;
        if (_schedulerWorkQueue is null)
            throw new InvalidOperationException("The checkpoint contains consumed scheduler work items but no scheduler work queue is configured.");

        foreach (var item in consumed)
        {
            var result = await _schedulerWorkQueue.ConsumeClaimedAsync(item, cancellationToken);
            if (!result.Succeeded)
                throw new RuntimeSchedulerWorkConsumeConflictException(item.WorkflowExecutionId, item.WorkItemId);
        }
    }

    private async ValueTask ValidateAlterationJobTerminalChangeAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.StateChanges.AlterationJobTerminalChange is not { } change)
            return;
        if (_alterationStore is null)
            throw new InvalidOperationException("A checkpoint carrying alteration-job terminal evidence requires an alteration store.");
        var job = await _alterationStore.FindJobAsync(change.JobId, cancellationToken)
                  ?? throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
        WorkflowAlterationTerminalEvidence.Validate(job, change, commit.WorkflowExecutionId);
    }

    private async ValueTask ApplyAlterationJobTerminalChangeAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.StateChanges.AlterationJobTerminalChange is not { } change)
            return;
        await _alterationStore!.ApplyTerminalJobChangeAsync(change, cancellationToken);
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
            RuntimeExecutionOwnershipStateId.For(commit.WorkflowExecutionId),
            cancellationToken);
        RuntimeExecutionFenceValidator.EnsureCurrent(commit.WorkflowExecutionId, expectedFence, state, _timeProvider.GetUtcNow());
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
                .OrderBy(RuntimePostCommitOutboxClaimTransitions.ClaimableAt)
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

    public async ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        await _state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var childExecution = completion.WorkflowDispatch is { } projectedDispatch &&
                                 _workflowExecutionStateStore is not null
                ? await _workflowExecutionStateStore.FindAsync(
                    projectedDispatch.ChildWorkflowExecutionId,
                    cancellationToken)
                : null;

            lock (_state.SyncRoot)
            {
                if (!_state.OutboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
                    throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");

                // Validate the claim/fence before considering lifecycle precedence. A stale claimant cannot turn a
                // newer generation into an acknowledgement merely because the deterministic child is now visible.
                var completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
                    existingOutbox,
                    completion.Claim,
                    completion.DeliveryResult);
                WorkflowDispatchRecord? winningDispatch = null;
                var admissionWins = false;
                if (completion.WorkflowDispatch is { } dispatch)
                {
                    if (!_state.WorkflowDispatches.TryGetValue(dispatch.DispatchId, out var existingDispatch))
                        throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the atomic checkpoint store.");

                    winningDispatch = WorkflowDispatchLifecycle.ResolveSuccessfulChildDelivery(
                        existingDispatch,
                        childExecution,
                        completion.DeliveryResult.RecordedAt);
                    admissionWins = winningDispatch is not null;
                    if (admissionWins)
                    {
                        completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
                            existingOutbox,
                            completion.Claim,
                            new RuntimePostCommitOutboxDeliveryResult(
                                completion.Claim.OutboxItemId,
                                RuntimePostCommitOutboxStatus.Delivered,
                                completion.DeliveryResult.RecordedAt));
                    }
                    else
                    {
                        WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, dispatch);
                        winningDispatch = dispatch;
                    }
                }

                if (!admissionWins && completion.FollowUpOutboxItem is { } followUp)
                {
                    if (StringComparer.Ordinal.Equals(followUp.OutboxItemId, completion.Claim.OutboxItemId))
                        throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
                    if (_state.OutboxItems.TryGetValue(followUp.OutboxItemId, out var existingFollowUp) &&
                        !existingFollowUp.IsEquivalentPendingItem(followUp))
                    {
                        throw new InvalidOperationException($"Post-commit follow-up item '{followUp.OutboxItemId}' already exists with conflicting state.");
                    }
                }

                _state.OutboxItems[completion.Claim.OutboxItemId] = completedOutbox;
                if (winningDispatch is not null)
                    _state.WorkflowDispatches[winningDispatch.DispatchId] = winningDispatch;
                if (!admissionWins && completion.FollowUpOutboxItem is { } followUpOutboxItem)
                    _state.OutboxItems.TryAdd(followUpOutboxItem.OutboxItemId, followUpOutboxItem);
            }
        }
        finally
        {
            _state.WriteGate.Release();
        }
    }

    internal ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        PersistenceAccessContext? accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            _state.WorkflowDispatches.TryGetValue(request.DispatchId, out var dispatch);
            if (dispatch is not null)
                accessContext?.EnsureTenantScope(dispatch.TenantId);
            RuntimePostCommitOutboxItem? deadLetter = null;
            var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
            if (deadLetterId is not null)
                _state.OutboxItems.TryGetValue(deadLetterId, out deadLetter);

            var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, deadLetter);
            if (transition.HasMutation)
            {
                _state.WorkflowDispatches[request.DispatchId] = transition.WorkflowDispatch!;
                _state.OutboxItems[transition.OutboxItem!.OutboxItemId] = transition.OutboxItem;
            }
            return ValueTask.FromResult(transition.Result);
        }
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
    /// Checks every pending outbox item in the commit against the items this store already holds before any state is
    /// projected or mutated (#386). This runs outside the inconsistent-durability guard so a data-conflict failure
    /// surfaces as a plain <see cref="InvalidOperationException"/> instead of being misclassified as a
    /// <see cref="RuntimeCheckpointInconsistentDurabilityException"/>. The item's own status and conflicts within the
    /// commit are structural rules <see cref="RuntimeCheckpointCommitValidator"/> already applied, so with commits
    /// serialized by the write gate nothing validation-shaped can throw inside the guarded mutation block.
    /// </summary>
    private void ValidatePendingOutboxItemsAgainstStore(IReadOnlyCollection<RuntimePostCommitOutboxItem> items)
    {
        lock (_state.SyncRoot)
        {
            foreach (var item in items)
            {
                if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing) && !existing.IsEquivalentPendingItem(item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
            }
        }
    }

    private void SavePendingOutboxItem(RuntimePostCommitOutboxItem item)
    {
        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing))
        {
            if (existing.IsEquivalentPendingItem(item))
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
                // The create-only insert is the atomic enforcement; the validation phase already refused a known conflict.
                if (!await _incidentStateStore.TryAddAsync(stateChange.State, cancellationToken))
                    throw IncidentStateTransitionValidator.AppendConflict(stateChange.State);

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

    private async ValueTask ApplyWorkflowDispatchCancellationsAsync(
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return;
        if (_workflowDispatchStore is not IWorkflowDispatchCancellationStore cancellationStore)
        {
            throw new InvalidOperationException(
                "Workflow dispatch cancellation requests require an IWorkflowDispatchCancellationStore.");
        }

        foreach (var request in requests)
            await cancellationStore.ApplyCancellationAsync(request, cancellationToken);
    }

    private async ValueTask ValidateWorkflowTestScopesAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        var execution = commit.StateChanges.WorkflowExecution?.State;
        // Only a root start can require an open scope, so only then is the execution's existence read.
        var executionExists = execution is { TestScope: not null, ParentWorkflowExecutionId: null } &&
                              _workflowExecutionStateStore is not null &&
                              await _workflowExecutionStateStore.FindAsync(execution.WorkflowExecutionId, cancellationToken) is not null;
        lock (_state.SyncRoot)
        {
            if (execution is not null &&
                WorkflowTestScopeAdmission.ScopeRequiredToStart(execution, executionExists) is { } rootScope)
                EnsureOpenScope(rootScope, commit.Checkpoint.OccurredAt);

            foreach (var change in commit.StateChanges.WorkflowDispatches)
            {
                if (WorkflowTestScopeAdmission.ScopeRequiredToAdd(change.State, _state.WorkflowDispatches.ContainsKey(change.StateId)) is { } scope)
                    EnsureOpenScope(scope, commit.Checkpoint.OccurredAt);
            }
        }
    }

    private void EnsureOpenScope(WorkflowTestScope scope, DateTimeOffset observedAt) =>
        WorkflowTestScopeAdmission.EnsureOpen(_state.WorkflowTestScopes.GetValueOrDefault(scope.ScopeId), scope, observedAt);

    /// <summary>
    /// Incident rules that read the incident this store holds: an Append must create it, and an Upsert must not change a
    /// committed resolution outcome. Checked before any write, inside this store's write gate.
    /// </summary>
    private async ValueTask ValidateIncidentChangesAgainstStoreAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            var existing = await _incidentStateStore.FindAsync(
                stateChange.State.WorkflowExecutionId,
                stateChange.State.IncidentId,
                cancellationToken);
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
                IncidentStateTransitionValidator.EnsureAppendTargetIsAbsent(existing, stateChange.State);
            else
                IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, stateChange.State);
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

        foreach (var stateChange in commit.StateChanges.WorkflowDispatches)
        {
            var existing = await _workflowDispatchStore.FindAsync(stateChange.StateId, cancellationToken);
            if (existing is not null)
                WorkflowDispatchLifecycle.ValidateTransition(existing, stateChange.State);
            else
                WorkflowDispatchLifecycle.ValidateNew(stateChange.State);
        }
    }

    private async ValueTask ValidateWorkflowDispatchCancellationsAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowDispatchCancellations.Count == 0)
            return;
        if (_workflowDispatchStore is not IWorkflowDispatchCancellationStore)
        {
            throw new InvalidOperationException(
                "Workflow dispatch cancellation requests require an IWorkflowDispatchCancellationStore.");
        }

        // Resolved here only to refuse an unresolvable request before any write; the cancellation store applies the same
        // resolution atomically.
        foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
            WorkflowDispatchLifecycle.ResolveParentCancellation(
                await _workflowDispatchStore.FindAsync(request.DispatchId, cancellationToken),
                request);
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
    IReadOnlyCollection<string> PendingPostCommitWorkIds,
    IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);
