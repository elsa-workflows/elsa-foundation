using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>Application-wide state shared by scoped in-memory checkpoint-store adapters.</summary>
public sealed class InMemoryRuntimeCheckpointStoreState
{
    private readonly AsyncLocal<int> _writeGateDepth = new();

    internal object SyncRoot { get; } = new();
    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal Dictionary<string, RuntimeCheckpointCommitRecord> Commits { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, RuntimePostCommitOutboxItem> OutboxItems { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowDispatchRecord> WorkflowDispatches { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, WorkflowTestScopeRecord> WorkflowTestScopes { get; } = new(StringComparer.Ordinal);
    internal Dictionary<(string Scope, string WorkflowExecutionId, string WorkId), BackgroundActivityWorkItem> BackgroundActivityWorkItems { get; } = [];

    internal async ValueTask<TResult> ExecuteWriteGateAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_writeGateDepth.Value > 0)
            return await action(cancellationToken);

        await WriteGate.WaitAsync(cancellationToken);
        _writeGateDepth.Value++;
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            _writeGateDepth.Value--;
            WriteGate.Release();
        }
    }
}

public sealed class InMemoryRuntimeCheckpointCommitStore : IRuntimeCheckpointCommitStore, IRuntimePostCommitOutboxStore, IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore, IStimulusDeliveryStore
{
    private readonly InMemoryRuntimeCheckpointStoreState _state;
    internal InMemoryRuntimeCheckpointStoreState State => _state;
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
    private readonly IPersistenceAccessContextAccessor? _persistenceAccessContextAccessor;

    /// <summary>
    /// Creates the in-memory commit store. Backing stores are optional for direct test construction. The runtime DI
    /// composition requires the workflow execution store for child-start evidence and injects the other registered
    /// backing stores. Coalescing decorators keep wrapping that registration without modification.
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
        IWorkflowAlterationStore? alterationStore = null,
        IPersistenceAccessContextAccessor? persistenceAccessContextAccessor = null)
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
        _persistenceAccessContextAccessor = persistenceAccessContextAccessor;
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
        return await _state.ExecuteWriteGateAsync(async ct =>
        {
            foreach (var change in commit.StateChanges.PostCommitOutbox)
                EnsureOutboxSourceScope(change.State);
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
                return await CommitNewAsync(commit, decision, ct);

            if (_operationalStateStore is not InMemoryExecutionLivenessStateStore inMemoryOperationalStateStore)
            {
                throw new InvalidOperationException(
                    "A fenced in-memory checkpoint requires the in-memory execution liveness-state store so ownership validation and checkpoint writes share one atomic boundary.");
            }

            return await inMemoryOperationalStateStore.ExecuteOwnershipAtomicAsync(
                token => CommitNewAsync(commit, decision, token),
                ct);
        }, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        await EnsureExpectedFenceAsync(commit, cancellationToken);
        await EnsureBackgroundActivityAdmissionsAreFencedAndLiveAsync(commit, cancellationToken);

        var stagedBackgroundActivityWork = PrepareBackgroundActivityWorkChanges(commit);

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
            // claimant's commit persists nothing.
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

                    if (stagedBackgroundActivityWork is not null)
                    {
                        _state.BackgroundActivityWorkItems.Clear();
                        foreach (var pair in stagedBackgroundActivityWork)
                            _state.BackgroundActivityWorkItems.Add(pair.Key, pair.Value);
                    }

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

    private async ValueTask EnsureBackgroundActivityAdmissionsAreFencedAndLiveAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.BackgroundActivityWork.Admissions.Count == 0)
            return;
        if (commit.ExpectedFence is null)
            throw new RuntimeCheckpointCommitValidationException("Background activity work admission requires the current workflow execution fence.");

        var ownership = await _operationalStateStore!.FindAsync(
            commit.WorkflowExecutionId,
            RuntimeExecutionOwnershipStateId.For(commit.WorkflowExecutionId),
            cancellationToken);
        if (ownership?.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var tombstone) == true)
        {
            if (!StringComparer.Ordinal.Equals(tombstone, "true"))
                throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted for a deleted workflow execution.");
        }

        if (_workflowExecutionStateStore is null)
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted without a workflow execution state store.");

        var hasLiveExecution = await _workflowExecutionStateStore.FindAsync(commit.WorkflowExecutionId, cancellationToken) is not null;
        var upsertInThisCommit = commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert } workflowChange &&
                                 StringComparer.Ordinal.Equals(workflowChange.StateId, commit.WorkflowExecutionId);
        if (!hasLiveExecution && !upsertInThisCommit)
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted without a live workflow execution state.");
    }

    private Dictionary<(string Scope, string WorkflowExecutionId, string WorkId), BackgroundActivityWorkItem>? PrepareBackgroundActivityWorkChanges(
        RuntimeCheckpointCommit commit)
    {
        var changes = commit.StateChanges.BackgroundActivityWork;
        if (changes.IsEmpty)
            return null;

        var scope = _persistenceAccessContextAccessor?.Current.RequireScope().Value ??
                    throw new InvalidOperationException("A checkpoint carrying background activity work requires a selected persistence scope.");
        Dictionary<(string Scope, string WorkflowExecutionId, string WorkId), BackgroundActivityWorkItem> staged;
        lock (_state.SyncRoot)
            staged = new Dictionary<(string Scope, string WorkflowExecutionId, string WorkId), BackgroundActivityWorkItem>(_state.BackgroundActivityWorkItems);

        foreach (var request in changes.Admissions)
        {
            var key = (scope, request.WorkflowExecutionId, request.WorkId);
            if (staged.ContainsKey(key))
                throw new InvalidOperationException($"Background activity work '{request.WorkId}' already exists in workflow execution '{request.WorkflowExecutionId}'.");
            staged.Add(key, BackgroundActivityWorkLifecycle.CreatePending(request, commit.Checkpoint.OccurredAt));
        }

        foreach (var acceptance in changes.Acceptances)
        {
            var key = (scope, acceptance.WorkflowExecutionId, acceptance.WorkId);
            if (!staged.TryGetValue(key, out var item))
                throw new InvalidOperationException($"Background activity work '{acceptance.WorkId}' was not found for receipt acceptance.");
            staged[key] = BackgroundActivityWorkLifecycle.Accept(item, acceptance);
        }

        // Acceptance and cancellation may be participants of the same terminal checkpoint: acceptance wins so
        // the subsequent workflow-wide cancellation preserves that durable receipt as Accepted.
        foreach (var cancellation in changes.Cancellations)
        {
            var matching = staged.Where(pair =>
                    StringComparer.Ordinal.Equals(pair.Key.Scope, scope) &&
                    StringComparer.Ordinal.Equals(pair.Key.WorkflowExecutionId, cancellation.WorkflowExecutionId) &&
                    (cancellation.ActivityExecutionIds is null || cancellation.ActivityExecutionIds.Contains(pair.Value.Request.ActivityExecutionId, StringComparer.Ordinal)) &&
                    pair.Value.Status is BackgroundActivityWorkStatus.Pending or BackgroundActivityWorkStatus.Claimed or BackgroundActivityWorkStatus.ResultReady)
                .ToArray();
            foreach (var pair in matching)
                staged[pair.Key] = BackgroundActivityWorkLifecycle.Cancel(pair.Value, commit.Checkpoint.OccurredAt);
        }

        return staged;
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

        // The lease is this attempt's alone (#2286). Two attempts of one commit (a retry that overlaps a slow first
        // attempt) would otherwise share one lease, and the first to release it would leave the other's closure unfenced.
        await _rootWriteLeaseManager.ExecuteAsync(
            workflowExecutionChange.State.PinnedExecutable,
            $"checkpoint:{commit.CommitId}:{Guid.NewGuid():N}",
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

    /// <summary>Test seam: seeds a pending outbox item directly, bypassing the commit path.</summary>
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

    public async ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(outboxItemId, cancellationToken);
        if (item is null) return null;
        var access = _persistenceAccessContextAccessor?.Current ??
            throw new InvalidOperationException("A durable source read requires explicit persistence access.");
        var scope = access.RequireScope();
        if (item.StimulusDeliverySourceScope != scope)
            throw new InvalidDataException("The in-memory source has no matching committed scope witness.");
        return item;
    }

    public async ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.MatchesSource(source))
            throw new ArgumentException("The proposed cohort does not match its source.", nameof(candidate));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _state.ExecuteWriteGateAsync(async ct =>
            {
                var access = _persistenceAccessContextAccessor?.Current ??
                    throw new InvalidOperationException("Recipient installation requires explicit persistence access.");
                var scope = access.RequireScope();
                access.EnsureScope(source.Scope);
                var publisher = _workflowExecutionStateStore is null ? null :
                    await _workflowExecutionStateStore.FindAsync(source.Intent.WorkflowExecutionId, ct);
                if (publisher is null)
                    throw new InvalidDataException("The authoritative publisher is unavailable or missing.");
                access.EnsureTenantScope(publisher.TenantId);
                lock (_state.SyncRoot)
                {
                    if (!_state.OutboxItems.TryGetValue(source.SourceOutboxItemId, out var current))
                        throw new InvalidDataException("The exact recipient source is missing.");
                    var installed = current.InstallRecipientSnapshot(source, candidate, publisher, scope, _timeProvider.GetUtcNow());
                    _state.OutboxItems[source.SourceOutboxItemId] = installed;
                    return installed.RecipientSnapshot!;
                }
            }, cancellationToken);
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception,
                exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException));
        }
    }

    public async ValueTask<PostCommitDeliveryContext> ReadSavedStartSourceAsync(WorkflowExecutableStimulusDeliveryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _state.ExecuteWriteGateAsync(async ct =>
            {
                var access = _persistenceAccessContextAccessor?.Current ?? throw new InvalidOperationException("Saved-start source reads require explicit persistence access.");
                var scope = access.RequireScope(); access.EnsureScope(authority.Scope);
                RuntimePostCommitOutboxItem initial;
                lock (_state.SyncRoot)
                    initial = _state.OutboxItems.GetValueOrDefault(authority.SourceOutboxItemId) ?? throw new InvalidDataException("The exact saved-start source is missing.");
                var publisher = _workflowExecutionStateStore is null ? null : await _workflowExecutionStateStore.FindAsync(initial.Intent.WorkflowExecutionId, ct);
                if (publisher is null) throw new InvalidDataException("The authoritative saved-start publisher is missing.");
                access.EnsureTenantScope(publisher.TenantId);
                lock (_state.SyncRoot)
                {
                    var current = _state.OutboxItems.GetValueOrDefault(authority.SourceOutboxItemId) ?? throw new InvalidDataException("The saved-start source was removed.");
                    return current.OpenSavedStartSource(authority, publisher, scope, _timeProvider.GetUtcNow());
                }
            }, cancellationToken);
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId, exception,
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
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

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(result.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found.");

            // Same ownership test the durable store applies, so both stores answer a contended claim-less recording
            // identically: write nothing and report the loss. The fencing token is checked before terminal state because
            // a completed owner still leaves its positive fence on the row; a claim-less caller does not hold that fence.
            if (existing.Status == RuntimePostCommitOutboxStatus.Delivering || existing.DeliveryFencingToken > 0)
                return new ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome>(
                    RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner);
            if (existing.IsTerminal)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");

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
                deliveryFencingToken: existing.DeliveryFencingToken,
                deliveryVisibleAfter: null,
                stimulusDeliveryProtocolVersion: existing.StimulusDeliveryProtocolVersion,
                stimulusDeliverySourceScope: existing.StimulusDeliverySourceScope,
                recipientSnapshot: existing.RecipientSnapshot);
        }

        return new ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome>(
            RuntimePostCommitOutboxClaimCompletionOutcome.Persisted);
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

    public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            if (!_state.OutboxItems.TryGetValue(claim.OutboxItemId, out var existing))
                return new ValueTask<RuntimePostCommitOutboxClaim?>((RuntimePostCommitOutboxClaim?)null);

            var renewed = RuntimePostCommitOutboxClaimTransitions.Renew(existing, claim, now, visibilityTimeout);
            if (renewed is not null)
                _state.OutboxItems[claim.OutboxItemId] = renewed.Item;
            return new ValueTask<RuntimePostCommitOutboxClaim?>(renewed);
        }
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
        RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(_state.OutboxItems.Values
                .Where(query.Matches)
                .OrderBy(RuntimePostCommitOutboxClaimTransitions.ClaimableAt)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToArray());
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

    public async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        return await _state.ExecuteWriteGateAsync(async ct =>
        {
            var childExecution = completion.WorkflowDispatch is { } projectedDispatch &&
                                 _workflowExecutionStateStore is not null
                ? await _workflowExecutionStateStore.FindAsync(
                    projectedDispatch.ChildWorkflowExecutionId,
                    ct)
                : null;

            lock (_state.SyncRoot)
            {
                if (!_state.OutboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
                    throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found.");
                WorkflowDispatchRecord? existingDispatch = null;
                if (completion.WorkflowDispatch is { } dispatch &&
                    !_state.WorkflowDispatches.TryGetValue(dispatch.DispatchId, out existingDispatch))
                {
                    throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the atomic checkpoint store.");
                }

                var resolution = RuntimePostCommitOutboxClaimTransitions.ResolveCompletion(
                    completion,
                    existingOutbox,
                    existingDispatch,
                    childExecution);
                if (resolution.FollowUpOutboxItem is { } followUp &&
                    _state.OutboxItems.TryGetValue(followUp.OutboxItemId, out var existingFollowUp) &&
                    !existingFollowUp.IsEquivalentPendingItem(followUp))
                {
                    throw new InvalidOperationException($"Post-commit follow-up item '{followUp.OutboxItemId}' already exists with conflicting state.");
                }

                _state.OutboxItems[completion.Claim.OutboxItemId] = resolution.OutboxItem;
                if (resolution.WorkflowDispatch is { } winningDispatch)
                    _state.WorkflowDispatches[winningDispatch.DispatchId] = winningDispatch;
                if (resolution.FollowUpOutboxItem is { } followUpOutboxItem)
                    _state.OutboxItems.TryAdd(followUpOutboxItem.OutboxItemId, followUpOutboxItem);
                return resolution.Outcome;
            }
        }, cancellationToken);
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
                EnsureOutboxSourceScope(item);
                if (_state.OutboxItems.TryGetValue(item.OutboxItemId, out var existing) && !existing.IsEquivalentPendingItem(item))
                    throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
            }
        }
    }

    private void EnsureOutboxSourceScope(RuntimePostCommitOutboxItem item)
    {
        if (item.StimulusDeliverySourceScope is not { } sourceScope) return;
        var access = _persistenceAccessContextAccessor?.Current ??
            throw new InvalidOperationException("A declared recipient source requires explicit persistence access at commit.");
        _ = access.RequireScope();
        access.EnsureScope(sourceScope);
    }

    private void SavePendingOutboxItem(RuntimePostCommitOutboxItem item)
    {
        EnsureOutboxSourceScope(item);
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
                (commit.Metadata.ContainsKey(TestRunDeliveryReceipt.MetadataKey) ? execution.TestScope :
                    WorkflowTestScopeAdmission.ScopeRequiredToStart(execution, executionExists)) is { } rootScope)
                EnsureOpenScope(rootScope, commit.Metadata.ContainsKey(TestRunDeliveryReceipt.MetadataKey) ? _timeProvider.GetUtcNow() : commit.Checkpoint.OccurredAt);

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
