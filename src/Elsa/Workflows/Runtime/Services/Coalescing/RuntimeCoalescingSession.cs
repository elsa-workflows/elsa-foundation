using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Per-drain in-memory working set for coalesced checkpoint segments (E3-6, RT-10). Buffers deferred checkpoint
/// change-sets, holds an overlay of the mutated continuation state so intra-segment reads see prior hops, mirrors the
/// scheduler work queue and post-commit outbox so the drain advances without touching durable stores, and folds
/// everything into a single atomic commit at a flush boundary.
/// </summary>
/// <remarks>
/// Crash-safety invariant: the durable scheduler queue's segment-entry items are never durably dequeued during the
/// segment — they are deleted only in <see cref="AdvanceInnerQueueAsync"/>, after the folded checkpoint commit lands.
/// A crash mid-segment therefore replays the whole segment from the last flushed state plus durable queue redelivery.
/// A session is single-writer (fenced by the ambient ownership lease) and confined to one drain; it is not thread-safe.
/// </remarks>
public sealed class RuntimeCoalescingSession
{
    private readonly IWorkflowSchedulerWorkQueue _innerQueue;
    private readonly InMemoryWorkflowSchedulerWorkQueue _overlayQueue = new();
    private readonly List<string> _seededWorkItemIds = [];
    private readonly ConcurrentDictionary<RuntimeSchedulerWorkClaim, byte> _overlayClaims =
        new(ReferenceEqualityComparer.Instance);
    private bool _queueSeeded;

    private readonly List<RuntimeCheckpointStateChangeSet> _bufferedChangeSets = [];
    private readonly List<RuntimeCheckpointPreparedCommit> _bufferedPreparedCommits = [];

    private WorkflowExecutionState? _workflowExecution;
    private SchedulerState? _scheduler;
    private readonly Dictionary<string, ActivityExecutionState> _activityUpserts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activityTombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DurableValueState> _durableValueUpserts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _durableValueTombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityExecutionInspectionProjection> _inspectionUpserts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityExecutionInspectionProjection?> _inspectionBaselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowDispatchRecord> _workflowDispatchUpserts = new(StringComparer.Ordinal);

    private readonly List<string> _outboxOrder = [];
    private readonly Dictionary<string, RuntimePostCommitOutboxItem> _outboxItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _durablyPersistedOutboxIds = new(StringComparer.Ordinal);
    private readonly IRuntimePostCommitOutboxStore? _innerOutboxStore;

    public RuntimeCoalescingSession(
        string workflowExecutionId,
        IWorkflowSchedulerWorkQueue innerQueue,
        CoalescingRuntimeCheckpointPersistenceOptions options,
        IRuntimePostCommitOutboxStore? innerOutboxStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(innerQueue);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxSegmentCheckpoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSegmentCheckpoints must be greater than zero.");

        WorkflowExecutionId = workflowExecutionId;
        _innerQueue = innerQueue;
        _innerOutboxStore = innerOutboxStore;
        MaxSegmentCheckpoints = options.MaxSegmentCheckpoints;
    }

    public string WorkflowExecutionId { get; }
    public int MaxSegmentCheckpoints { get; }

    /// <summary>
    /// <see langword="true"/> while the session is coalescing. An activity-attempt boundary and the per-segment hop cap
    /// durably close one segment and start another in the same drain; terminal/external boundaries and quiescence
    /// deactivate the session.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public int HopCount { get; private set; }
    public bool HasBufferedChanges => _bufferedChangeSets.Count > 0;
    public IReadOnlyList<RuntimeCheckpointPreparedCommit> BufferedPreparedCommits => _bufferedPreparedCommits;

    public void Deactivate() => IsActive = false;

    /// <summary>Returns <see langword="true"/> when this session owns the supplied workflow execution and is active.</summary>
    public bool AppliesTo(string workflowExecutionId) =>
        IsActive && StringComparer.Ordinal.Equals(WorkflowExecutionId, workflowExecutionId);

    /// <summary>Returns whether this exact claim was issued by the overlay queue.</summary>
    public bool OwnsOverlayClaim(RuntimeSchedulerWorkClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return _overlayClaims.ContainsKey(claim);
    }

    // ---- Buffering -------------------------------------------------------------------------------------------------

    /// <summary>Applies a deferred commit to the overlay working set and buffers its change-set for the eventual fold.</summary>
    public void BufferDeferred(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        ApplyToOverlay(commit.StateChanges, includeOutbox: true);
        _bufferedChangeSets.Add(commit.StateChanges);
        HopCount++;
    }

    /// <summary>Retains the provider-owned preparation authority together with its deterministic final replay.</summary>
    public void BufferDeferred(RuntimeCheckpointPreparedCommit preparedCommit)
    {
        ArgumentNullException.ThrowIfNull(preparedCommit);
        if (!StringComparer.Ordinal.Equals(preparedCommit.Commit.WorkflowExecutionId, WorkflowExecutionId))
            throw new InvalidOperationException("A coalescing session cannot retain a preparation for another workflow execution.");
        if (_bufferedPreparedCommits.Count > 0 &&
            preparedCommit.Token.Provenance.WorkflowCheckpointOrder <=
            _bufferedPreparedCommits[^1].Token.Provenance.WorkflowCheckpointOrder)
        {
            throw new InvalidOperationException("Prepared checkpoints must be retained in strictly increasing allocated order.");
        }

        ApplyToOverlay(preparedCommit.Commit.StateChanges, includeOutbox: true);
        _bufferedChangeSets.Add(preparedCommit.Commit.StateChanges);
        _bufferedPreparedCommits.Add(preparedCommit);
        HopCount++;
    }

    /// <summary>
    /// Advances the state overlay to a boundary that has already committed durably. Boundary-owned outbox items remain
    /// with the durable provider; only deferred outbox items belong to the in-memory segment overlay.
    /// </summary>
    public void RecordDurableBoundaryState(RuntimeCheckpointStateChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ApplyToOverlay(changes, includeOutbox: false);
    }

    /// <summary>
    /// Advances the overlay past a cap-hit fold-and-flush whose trailing commit stays inside the still-active session.
    /// Unlike <see cref="RecordDurableBoundaryState"/>, the trailing commit's outbox items enter the overlay too: the
    /// drain keeps delivering continuation from the working set for the next segment, while the flush already persisted
    /// those items durably as the crash-redrive guarantee.
    /// </summary>
    public void RecordCapFlushState(RuntimeCheckpointStateChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ApplyToOverlay(changes, includeOutbox: true);
    }

    /// <summary>
    /// Records that a mid-drain flush durably persisted these outbox items while the session stays active. Their
    /// authoritative lifecycle continues in the overlay; <see cref="ReconcileDurablyPersistedOutboxAsync"/> writes the
    /// overlay outcome back to the durable store at the next flush so no durable <c>Pending</c> residue survives the
    /// drain and gets redelivered as a duplicate by a later sweep.
    /// </summary>
    public void MarkOutboxDurablyPersisted(IEnumerable<RuntimeStateChange<RuntimePostCommitOutboxItem>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
            _durablyPersistedOutboxIds.Add(change.StateId);
    }

    /// <summary>Whether a mid-drain flush already durably persisted this outbox item.</summary>
    public bool IsOutboxDurablyPersisted(string outboxItemId) => _durablyPersistedOutboxIds.Contains(outboxItemId);

    /// <summary>
    /// Whether any durably persisted outbox item has since reached a completed overlay outcome that the durable store
    /// does not know about yet. When <see langword="true"/> the drain needs one more flush even with an empty buffer.
    /// </summary>
    public bool RequiresDurableOutboxReconciliation =>
        _durablyPersistedOutboxIds.Any(id => _outboxItems[id].Status
            is RuntimePostCommitOutboxStatus.Delivered
            or RuntimePostCommitOutboxStatus.FailedFinal);

    /// <summary>
    /// Writes the overlay delivery outcome of previously durably persisted outbox items back to the durable inner
    /// outbox store. Called only as part of a flush, after the folded checkpoint commit has landed: reconciling earlier
    /// would mark an item Delivered durably while its downstream effect (the overlay-enqueued continuation) is still
    /// memory-only, destroying the crash-redrive guarantee. Items still pending in the overlay stay tracked — their
    /// durable <c>Pending</c> row remains the crash backstop until they resolve or the drain ends.
    /// </summary>
    public async ValueTask ReconcileDurablyPersistedOutboxAsync(DateTimeOffset recordedAtFallback, CancellationToken cancellationToken)
    {
        if (_durablyPersistedOutboxIds.Count == 0)
            return;

        List<string>? reconciled = null;
        foreach (var outboxItemId in _durablyPersistedOutboxIds)
        {
            var item = _outboxItems[outboxItemId];
            if (item.Status is not (RuntimePostCommitOutboxStatus.Delivered or RuntimePostCommitOutboxStatus.FailedFinal))
                continue;

            if (_innerOutboxStore is null)
            {
                throw new InvalidOperationException(
                    $"Post-commit outbox item '{outboxItemId}' was durably persisted mid-drain but the coalescing session has no inner outbox store to reconcile its delivery outcome.");
            }

            await _innerOutboxStore.RecordDeliveryResultAsync(
                new RuntimePostCommitOutboxDeliveryResult(
                    outboxItemId,
                    item.Status,
                    item.DeliveredAt ?? recordedAtFallback,
                    item.Status == RuntimePostCommitOutboxStatus.FailedFinal
                        ? item.LastFailureMessage ?? "Coalesced post-commit delivery failed."
                        : null),
                cancellationToken);
            (reconciled ??= []).Add(outboxItemId);
        }

        if (reconciled is not null)
        {
            foreach (var outboxItemId in reconciled)
                _durablyPersistedOutboxIds.Remove(outboxItemId);
        }
    }

    private void ApplyToOverlay(RuntimeCheckpointStateChangeSet changes, bool includeOutbox)
    {
        if (changes.WorkflowExecution is { } workflowExecutionChange)
            _workflowExecution = workflowExecutionChange.State;

        if (changes.Scheduler is { } schedulerChange)
            _scheduler = schedulerChange.State;

        foreach (var change in changes.ActivityExecutions)
        {
            if (change.Operation == RuntimeStateChangeOperation.Delete)
            {
                _activityUpserts.Remove(change.StateId);
                _activityTombstones.Add(change.StateId);
            }
            else
            {
                _activityTombstones.Remove(change.StateId);
                _activityUpserts[change.StateId] = change.State;
            }
        }

        foreach (var change in changes.DurableValues)
        {
            if (change.Operation == RuntimeStateChangeOperation.Delete)
            {
                _durableValueUpserts.Remove(change.StateId);
                _durableValueTombstones.Add(change.StateId);
            }
            else
            {
                _durableValueTombstones.Remove(change.StateId);
                _durableValueUpserts[change.StateId] = change.State;
            }
        }

        foreach (var change in changes.ActivityExecutionInspections)
            _inspectionUpserts[change.StateId] = change.State;

        foreach (var change in changes.WorkflowDispatches)
            _workflowDispatchUpserts[change.StateId] = change.State;

        if (includeOutbox)
        {
            foreach (var change in changes.PostCommitOutbox)
                RecordOutboxItem(change.State);
        }
    }

    private void RecordOutboxItem(RuntimePostCommitOutboxItem item)
    {
        if (_outboxItems.ContainsKey(item.OutboxItemId))
            return;

        _outboxItems.Add(item.OutboxItemId, item);
        _outboxOrder.Add(item.OutboxItemId);
    }

    // ---- Inspection durable-baseline memo --------------------------------------------------------------------------
    // Store-read coalescing for IActivityExecutionInspectionStore.FindAsync (spec-110 family): the memo holds what the
    // durable store returned (a cached null records a durable miss), never the buffered overlay state, so a memoized
    // read is byte-identical to a fresh durable read. Validity rests on the single-writer ownership lease: while the
    // session is active, the only path that can change durable inspection rows is this session's own flush, and every
    // flush invalidates the memo via InvalidateInspectionBaselines.

    /// <summary>Returns a memoized durable-baseline inspection read. A hit may carry <see langword="null"/> (a cached durable miss).</summary>
    public bool TryGetInspectionBaseline(string activityExecutionId, out ActivityExecutionInspectionProjection? projection) =>
        _inspectionBaselines.TryGetValue(activityExecutionId, out projection);

    /// <summary>Memoizes the result of a durable inspection read (including a null miss) until the next durable flush.</summary>
    public void CacheInspectionBaseline(string activityExecutionId, ActivityExecutionInspectionProjection? projection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        _inspectionBaselines[activityExecutionId] = projection;
    }

    /// <summary>Drops all memoized baselines. Must be called whenever a durable checkpoint commit lands for this session.</summary>
    public void InvalidateInspectionBaselines() => _inspectionBaselines.Clear();

    // ---- State overlay reads ---------------------------------------------------------------------------------------

    public bool TryGetWorkflowExecution(string workflowExecutionId, out WorkflowExecutionState? state)
    {
        if (_workflowExecution is not null && StringComparer.Ordinal.Equals(_workflowExecution.WorkflowExecutionId, workflowExecutionId))
        {
            state = _workflowExecution;
            return true;
        }

        state = null;
        return false;
    }

    public bool TryGetScheduler(string workflowExecutionId, out SchedulerState? state)
    {
        if (_scheduler is not null && StringComparer.Ordinal.Equals(_scheduler.WorkflowExecutionId, workflowExecutionId))
        {
            state = _scheduler;
            return true;
        }

        state = null;
        return false;
    }

    public bool TryGetActivity(string activityExecutionId, out ActivityExecutionState? state, out bool tombstoned)
    {
        if (_activityUpserts.TryGetValue(activityExecutionId, out var found))
        {
            state = found;
            tombstoned = false;
            return true;
        }

        if (_activityTombstones.Contains(activityExecutionId))
        {
            state = null;
            tombstoned = true;
            return true;
        }

        state = null;
        tombstoned = false;
        return false;
    }

    public bool TryGetDurableValue(string durableValueId, out DurableValueState? state, out bool tombstoned)
    {
        if (_durableValueUpserts.TryGetValue(durableValueId, out var found))
        {
            state = found;
            tombstoned = false;
            return true;
        }

        if (_durableValueTombstones.Contains(durableValueId))
        {
            state = null;
            tombstoned = true;
            return true;
        }

        state = null;
        tombstoned = false;
        return false;
    }

    /// <summary>Returns a stable snapshot of activity upserts currently visible through this session.</summary>
    public IReadOnlyCollection<ActivityExecutionState> GetActivityUpserts() => _activityUpserts.Values.ToArray();

    /// <summary>Returns a stable snapshot of activity identities deleted by this session.</summary>
    public IReadOnlyCollection<string> GetActivityTombstones() => _activityTombstones.ToArray();

    /// <summary>Returns a stable snapshot of durable-value upserts currently visible through this session.</summary>
    public IReadOnlyCollection<DurableValueState> GetDurableValueUpserts() => _durableValueUpserts.Values.ToArray();

    /// <summary>Returns a stable snapshot of durable-value identities deleted by this session.</summary>
    public IReadOnlyCollection<string> GetDurableValueTombstones() => _durableValueTombstones.ToArray();

    /// <summary>Merges an inner activity-execution list with the overlay upserts and tombstones for this workflow execution.</summary>
    public IReadOnlyCollection<ActivityExecutionState> MergeActivityList(IReadOnlyCollection<ActivityExecutionState> innerList)
    {
        var merged = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);

        foreach (var state in innerList)
            merged[state.Execution.ActivityExecutionId] = state;

        foreach (var tombstoned in _activityTombstones)
            merged.Remove(tombstoned);

        foreach (var (id, state) in _activityUpserts)
        {
            if (StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, WorkflowExecutionId))
                merged[id] = state;
        }

        return merged.Values.ToArray();
    }

    /// <summary>Merges an inner durable-value list with the overlay upserts and tombstones for this workflow execution.</summary>
    public IReadOnlyCollection<DurableValueState> MergeDurableValueList(IReadOnlyCollection<DurableValueState> innerList)
    {
        var merged = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);

        foreach (var state in innerList)
            merged[state.DurableValueId] = state;

        foreach (var tombstoned in _durableValueTombstones)
            merged.Remove(tombstoned);

        foreach (var (id, state) in _durableValueUpserts)
        {
            if (StringComparer.Ordinal.Equals(state.WorkflowExecutionId, WorkflowExecutionId))
                merged[id] = state;
        }

        return merged.Values.ToArray();
    }

    // ---- Outbox overlay --------------------------------------------------------------------------------------------

    public IReadOnlyCollection<RuntimePostCommitOutboxItem> GetDeliverableOutbox(RuntimePostCommitOutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Stable sort over the insertion-ordered working set: items recorded in the same burst tick (equal
        // AvailableAt/RecordedAt — the common case inside a coalesced segment) deliver in causal recording order, not
        // arbitrary id order. With strictly increasing timestamps this is byte-identical to ordering by RecordedAt.
        return _outboxOrder
            .Select(id => _outboxItems[id])
            .Where(item => IsDeliverable(item, query))
            .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RecordedAt)
            .Take(query.Limit)
            .ToArray();
    }

    private bool IsDeliverable(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        if (item.Status is not (RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.FailedRetryable))
            return false;

        if (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
            item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount))
            return false;

        if (query.WorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, query.WorkflowExecutionId))
            return false;

        if (query.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, query.IntentKind))
            return false;

        return (item.AvailableAt ?? DateTimeOffset.MinValue) <= query.Now;
    }

    public bool OwnsOutboxItem(string outboxItemId) => _outboxItems.ContainsKey(outboxItemId);

    public bool TryFindOutboxItem(string outboxItemId, out RuntimePostCommitOutboxItem? item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        return _outboxItems.TryGetValue(outboxItemId, out item);
    }

    public IReadOnlyCollection<RuntimePostCommitOutboxClaim> ClaimOutbox(RuntimePostCommitOutboxClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Same stable causal ordering as GetDeliverableOutbox: same-tick items claim in recording order.
        var claimable = _outboxOrder
            .Select(id => _outboxItems[id])
            .Where(item => RuntimePostCommitOutboxClaimTransitions.CanClaim(item, request))
            .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RecordedAt)
            .Take(request.Limit)
            .ToArray();
        var claims = new RuntimePostCommitOutboxClaim[claimable.Length];
        for (var index = 0; index < claimable.Length; index++)
        {
            var claim = RuntimePostCommitOutboxClaimTransitions.Claim(claimable[index], request);
            _outboxItems[claim.OutboxItemId] = claim.Item;
            claims[index] = claim;
        }

        return claims;
    }

    public void RecordOutboxDelivery(RuntimePostCommitOutboxDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!_outboxItems.TryGetValue(result.OutboxItemId, out var existing))
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found in the coalescing session.");
        if (existing.Status == RuntimePostCommitOutboxStatus.Delivering)
            throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is claimed; its owner and fencing token are required.");

        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(existing.DeliveryAttemptCount);
        var status = result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                     existing.RetryPolicy.IsExhaustedAfterAttempt(attemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : result.Status;
        _outboxItems[result.OutboxItemId] = new RuntimePostCommitOutboxItem(
            outboxItemId: existing.OutboxItemId,
            intent: existing.Intent,
            status: status,
            recordedAt: existing.RecordedAt,
            availableAt: status == RuntimePostCommitOutboxStatus.FailedRetryable
                ? result.RecordedAt.Add(existing.RetryPolicy.Delay ?? TimeSpan.Zero)
                : null,
            retryPolicy: existing.RetryPolicy,
            deliveryAttemptCount: attemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            lastFailureMessage: result.FailureMessage,
            metadata: existing.Metadata,
            deliveryFencingToken: existing.DeliveryFencingToken);
    }

    public void RecordOutboxDelivery(RuntimePostCommitOutboxClaim claim, RuntimePostCommitOutboxDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        if (!_outboxItems.TryGetValue(claim.OutboxItemId, out var existing))
            throw new InvalidOperationException($"Post-commit outbox item '{claim.OutboxItemId}' was not found in the coalescing session.");

        _outboxItems[claim.OutboxItemId] = RuntimePostCommitOutboxClaimTransitions.Complete(existing, claim, result);
    }

    public void CompleteOutboxClaim(RuntimePostCommitOutboxClaimCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!_outboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
            throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found in the coalescing session.");

        var completedOutbox = RuntimePostCommitOutboxClaimTransitions.Complete(
            existingOutbox,
            completion.Claim,
            completion.DeliveryResult);
        if (completion.WorkflowDispatch is { } dispatch)
        {
            if (!_workflowDispatchUpserts.TryGetValue(dispatch.DispatchId, out var existingDispatch))
                throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the coalescing session.");
            WorkflowDispatchLifecycle.ValidateTransition(existingDispatch, dispatch);
        }
        if (completion.FollowUpOutboxItem is { } followUp)
        {
            if (StringComparer.Ordinal.Equals(followUp.OutboxItemId, completion.Claim.OutboxItemId))
                throw new InvalidOperationException("A post-commit follow-up cannot replace the claimed outbox item.");
            if (_outboxItems.TryGetValue(followUp.OutboxItemId, out var existingFollowUp) &&
                !PendingOutboxItemsEquivalent(existingFollowUp, followUp))
            {
                throw new InvalidOperationException($"Post-commit follow-up item '{followUp.OutboxItemId}' already exists with conflicting state.");
            }
        }

        _outboxItems[completion.Claim.OutboxItemId] = completedOutbox;
        if (completion.WorkflowDispatch is { } workflowDispatch)
            _workflowDispatchUpserts[workflowDispatch.DispatchId] = workflowDispatch;
        if (completion.FollowUpOutboxItem is { } followUpOutboxItem)
        {
            if (_outboxItems.TryAdd(followUpOutboxItem.OutboxItemId, followUpOutboxItem))
                _outboxOrder.Add(followUpOutboxItem.OutboxItemId);
        }
    }

    private static bool PendingOutboxItemsEquivalent(
        RuntimePostCommitOutboxItem left,
        RuntimePostCommitOutboxItem right) =>
        left.Status == RuntimePostCommitOutboxStatus.Pending &&
        right.Status == RuntimePostCommitOutboxStatus.Pending &&
        left.RecordedAt == right.RecordedAt &&
        left.AvailableAt == right.AvailableAt &&
        left.DeliveryAttemptCount == right.DeliveryAttemptCount &&
        left.DeliveryFencingToken == right.DeliveryFencingToken &&
        left.RetryPolicy.IsEquivalentTo(right.RetryPolicy) &&
        StringComparer.Ordinal.Equals(left.Intent.IntentId, right.Intent.IntentId) &&
        StringComparer.Ordinal.Equals(left.Intent.WorkflowExecutionId, right.Intent.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.Intent.Kind, right.Intent.Kind) &&
        StringComparer.Ordinal.Equals(left.Intent.ActivityExecutionId, right.Intent.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.Intent.IdempotencyKey, right.Intent.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(left.Intent.DependsOnWaitRegistrationId, right.Intent.DependsOnWaitRegistrationId) &&
        left.Intent.WaitFailurePolicy == right.Intent.WaitFailurePolicy &&
        NullableJsonEquals(left.Intent.Payload, right.Intent.Payload) &&
        MetadataEquals(left.Intent.Metadata, right.Intent.Metadata) &&
        MetadataEquals(left.Metadata, right.Metadata);

    private static bool NullableJsonEquals(System.Text.Json.JsonElement? left, System.Text.Json.JsonElement? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue || StringComparer.Ordinal.Equals(left.Value.GetRawText(), right!.Value.GetRawText()));

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(item => right.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));

    // ---- Queue overlay ---------------------------------------------------------------------------------------------

    /// <summary>Copies the durable inner queue's current items for this workflow execution into the overlay once.</summary>
    public async ValueTask EnsureQueueSeededAsync(CancellationToken cancellationToken)
    {
        if (_queueSeeded)
            return;

        _queueSeeded = true;

        var innerItems = await _innerQueue.ListAllAsync(WorkflowExecutionId, cancellationToken);
        foreach (var item in innerItems)
        {
            await _overlayQueue.EnqueueAsync(item, cancellationToken);
            _seededWorkItemIds.Add(item.WorkItemId);
        }
    }

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueOverlayAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        return await _overlayQueue.EnqueueAsync(workItem, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListOverlayAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        return await _overlayQueue.ListAsync(query, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueOverlayAsync(CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        return await _overlayQueue.DequeueAsync(WorkflowExecutionId, cancellationToken);
    }

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimOverlayAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        var claim = await _overlayQueue.ClaimAsync(request, cancellationToken);
        if (claim is not null)
            _overlayClaims.TryAdd(claim, 0);
        return claim;
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewOverlayClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken)
    {
        var result = await _overlayQueue.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
        _overlayClaims.TryRemove(claim, out _);
        if (result.Claim is not null)
            _overlayClaims.TryAdd(result.Claim, 0);
        return result;
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteOverlayClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken)
    {
        var result = await _overlayQueue.CompleteClaimAsync(claim, cancellationToken);
        _overlayClaims.TryRemove(claim, out _);
        return result;
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseOverlayClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken)
    {
        var result = await _overlayQueue.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
        _overlayClaims.TryRemove(claim, out _);
        return result;
    }

    // ---- Inline fused pump (spec 123 D2) ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the FIFO-earliest overlay item not currently claimed in flight (spec 123 D2). The drain loop's claim on
    /// the fused span's originating <c>ScheduleActivity</c> stays at the queue head for the whole span, so the inline
    /// completion pump reads past live claims in true insertion order — the exact order the drain loop would claim
    /// next. The item is not claimed: the session is single-writer and drain-confined, so the pump dispatches it inline
    /// and then removes it with <see cref="ConsumePumpedOverlayItemAsync"/>. An item still in dispatch at a mid-drain
    /// flush counts as remaining continuation (the durable crash backstop) exactly like any unclaimed overlay item.
    /// </summary>
    public async ValueTask<RuntimeSchedulerWorkItem?> PeekNextPumpableOverlayItemAsync(CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        var inFlight = _overlayClaims.Keys
            .Select(claim => claim.Item.WorkItemId)
            .ToHashSet(StringComparer.Ordinal);
        return await _overlayQueue.PeekFirstAvailableAsync(WorkflowExecutionId, inFlight, cancellationToken);
    }

    /// <summary>
    /// Removes an inline-pumped item from the overlay after its dispatch completed (spec 123 D2). Consumption is
    /// FIFO-after-the-claimed-head, so the flush accounting in <see cref="AdvanceInnerQueueAsync"/> — which excludes
    /// in-flight claims before counting the consumed seeded prefix — still sees a strict FIFO prefix consumed.
    /// </summary>
    public async ValueTask<bool> ConsumePumpedOverlayItemAsync(string workItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        await EnsureQueueSeededAsync(cancellationToken);
        return await _overlayQueue.DeleteAsync(WorkflowExecutionId, workItemId, cancellationToken);
    }

    // ---- Flush -----------------------------------------------------------------------------------------------------

    /// <summary>Folds the buffered change-sets (state only) into one change-set. Callers set the outbox separately.</summary>
    public RuntimeCheckpointStateChangeSet FoldBufferedStateChanges() =>
        WithWorkflowDispatchOverlay(RuntimeCheckpointFold.Fold(_bufferedChangeSets));

    /// <summary>Folds the buffered change-sets with a trailing boundary change-set applied last (state only).</summary>
    public RuntimeCheckpointStateChangeSet FoldBufferedStateChangesWith(RuntimeCheckpointStateChangeSet trailing)
    {
        ArgumentNullException.ThrowIfNull(trailing);
        return WithWorkflowDispatchOverlay(RuntimeCheckpointFold.Fold([.. _bufferedChangeSets, trailing]));
    }

    /// <summary>
    /// Creates the exact terminal fold for the current prepared segment, optionally including one trailing boundary.
    /// Every logical member keeps its own explicit disposition while the provider receives one folded state/outbox
    /// application for the successful transaction.
    /// </summary>
    public RuntimeCheckpointPreparedFoldRequest CreatePreparedFoldRequest(
        RuntimeCheckpointPreparedCommit? trailing = null)
    {
        if (_bufferedPreparedCommits.Count == 0 && trailing is null)
            throw new InvalidOperationException("A prepared fold requires at least one logical checkpoint.");

        var prepared = trailing is null
            ? _bufferedPreparedCommits.ToArray()
            : [.. _bufferedPreparedCommits, trailing];
        var members = prepared.Select(item => new RuntimeCheckpointPreparedFoldMember(
            item.Token,
            item.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip
                ? RuntimeCheckpointPreparedDisposition.Skipped
                : RuntimeCheckpointPreparedDisposition.Committed,
            item.CurrentAuthorityFence,
            item.AuthorityRevision,
            item)).ToArray();

        var folded = trailing is null
            ? FoldBufferedStateChanges()
            : trailing.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip
                ? FoldBufferedStateChanges()
                : FoldBufferedStateChangesWith(trailing.Commit.StateChanges);
        var pendingOutbox = RemainingPendingOutboxChanges()
            .Concat(trailing is null || trailing.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip
                ? []
                : trailing.Commit.StateChanges.PostCommitOutbox)
            .GroupBy(change => change.StateId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        folded = folded.WithPostCommitOutbox(pendingOutbox);
        var retainedOutboxIds = pendingOutbox
            .Select(change => change.State.OutboxItemId)
            .ToHashSet(StringComparer.Ordinal);
        var deliveredOutboxIds = members
            .Where(member => member.Disposition == RuntimeCheckpointPreparedDisposition.Committed)
            .SelectMany(member => member.PreparedCommit!.Commit.StateChanges.PostCommitOutbox)
            .Select(change => change.State.OutboxItemId)
            .Where(id => !retainedOutboxIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new RuntimeCheckpointPreparedFoldRequest(
            WorkflowExecutionId,
            members,
            members[^1].WorkflowCheckpointOrder,
            folded,
            members[0].ExpectedCurrentAuthorityFence,
            DeliveredPostCommitOutboxItemIds: deliveredOutboxIds);
    }

    private RuntimeCheckpointStateChangeSet WithWorkflowDispatchOverlay(RuntimeCheckpointStateChangeSet folded)
    {
        if (_workflowDispatchUpserts.Count == 0)
            return folded;

        var merged = folded.WorkflowDispatches.ToDictionary(change => change.StateId, StringComparer.Ordinal);
        foreach (var record in _workflowDispatchUpserts.Values)
        {
            merged[record.DispatchId] = new RuntimeStateChange<WorkflowDispatchRecord>(
                record.DispatchId,
                RuntimeStateChangeOperation.Upsert,
                record,
                new Dictionary<string, string>());
        }

        return folded.WithWorkflowDispatches(merged.Values.ToArray());
    }

    /// <summary>The still-undelivered outbox items, as change-set entries, to persist atomically in the flush commit.</summary>
    public IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> RemainingPendingOutboxChanges() =>
        _outboxOrder
            .Select(id => _outboxItems[id])
            .Where(item => item.Status is RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.FailedRetryable)
            .Select(item => new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                item.OutboxItemId,
                RuntimeStateChangeOperation.Upsert,
                item,
                item.Metadata))
            .ToArray();

    public void ClearBuffer()
    {
        _bufferedChangeSets.Clear();
        _bufferedPreparedCommits.Clear();
        HopCount = 0;
    }

    /// <summary>
    /// Advances the durable inner queue to reflect the coalesced segment: deletes the consumed segment-entry items
    /// (a FIFO prefix of the seeded items) and durably enqueues any remaining unconsumed continuation. Called only as
    /// part of a flush, after the folded checkpoint commit has landed durably (condition B).
    /// </summary>
    /// <param name="consumeInFlightClaims">
    /// The overlay lists a claimed work item until its claim completes, so the item whose dispatch triggered this very
    /// flush is still visible here. When the flush already carries that item's durable effect — a boundary that
    /// deactivates coalescing (suspension, terminal, quiescence) or a cap-hit fold-and-flush whose trailing commit
    /// rode the in-flight dispatch — pass <see langword="true"/>: keeping a durable copy would redeliver an
    /// already-applied item (to the still-live drain loop after deactivation, which poisons on the replay-conflict
    /// tripwire, or on crash recovery mid-drain, where the flushed pending outbox items are the crash-redrive
    /// guarantee instead). When the session continues past a durable attempt boundary that precedes the in-flight
    /// user code, pass <see langword="false"/>: there the durable copy is the crash-redrive guarantee for that user
    /// code and the next flush counts it consumed once its claim completes.
    /// </param>
    public async ValueTask AdvanceInnerQueueAsync(bool consumeInFlightClaims, CancellationToken cancellationToken)
    {
        if (!_queueSeeded)
            return;

        var seeded = new HashSet<string>(_seededWorkItemIds, StringComparer.Ordinal);
        IReadOnlyCollection<RuntimeSchedulerWorkItem> remaining =
            await _overlayQueue.ListAllAsync(WorkflowExecutionId, cancellationToken);

        if (consumeInFlightClaims && _overlayClaims.Count > 0)
        {
            var inFlight = new HashSet<string>(
                _overlayClaims.Keys.Select(claim => claim.Item.WorkItemId),
                StringComparer.Ordinal);
            remaining = remaining.Where(item => !inFlight.Contains(item.WorkItemId)).ToArray();
        }

        var remainingSeeded = remaining.Count(item => seeded.Contains(item.WorkItemId));
        var consumedSeeded = _seededWorkItemIds.Count - remainingSeeded;

        for (var i = 0; i < consumedSeeded; i++)
            await _innerQueue.DequeueAsync(WorkflowExecutionId, cancellationToken);

        foreach (var item in remaining)
        {
            if (!seeded.Contains(item.WorkItemId))
                await _innerQueue.EnqueueAsync(item, cancellationToken);
        }

        // The durable queue now matches the overlay's remaining frontier. Treat that frontier as the entry point for
        // a possible next coalesced segment in this drain so a later boundary advances only work consumed since here.
        _seededWorkItemIds.Clear();
        _seededWorkItemIds.AddRange(remaining.Select(item => item.WorkItemId));
    }
}
