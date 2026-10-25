using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Per-drain in-memory working set for coalesced checkpoint segments. Buffers deferred checkpoint
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
    private readonly List<string> _durableWorkItemIds = [];
    private readonly HashSet<string> _knownOverlayWorkItemIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeSchedulerWorkItem> _activeFusedScheduleAnchors = new(StringComparer.Ordinal);
    private readonly List<string> _activeFusedScheduleAnchorOrder = [];
    private readonly ConcurrentDictionary<RuntimeSchedulerWorkClaim, TimeSpan> _overlayClaims =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, DateTimeOffset?> _durableVisibility = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingRelease> _pendingReleases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _durablyConsumedWorkItemIds = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private bool _queueSeeded;
    private bool _durableSchedulerClaimBoundaryRequested;

    private readonly List<RuntimeCheckpointStateChangeSet> _bufferedChangeSets = [];
    private readonly HashSet<string> _segmentActivityIds = new(StringComparer.Ordinal);

    private WorkflowExecutionState? _workflowExecution;
    private SchedulerState? _scheduler;
    private readonly Dictionary<string, ActivityExecutionState> _activityUpserts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activityTombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DurableValueState> _durableValueUpserts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _durableValueTombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityExecutionInspectionProjection> _inspectionUpserts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowDispatchRecord> _workflowDispatchUpserts = new(StringComparer.Ordinal);

    private readonly List<string> _outboxOrder = [];
    private readonly Dictionary<string, RuntimePostCommitOutboxItem> _outboxItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _durablyPersistedOutboxIds = new(StringComparer.Ordinal);
    private bool _mayReceiveDurableSchedulerDeliveries;
    private readonly IRuntimePostCommitOutboxStore? _innerOutboxStore;
    private readonly RuntimeCoalescingDurableValuePageMemo? _durableValuePageMemo;

    /// <param name="workflowExecutionId">The run this session folds for.</param>
    /// <param name="innerQueue">The durable scheduler queue the overlay is seeded from and advanced into.</param>
    /// <param name="options">The host's persistence-mode settings.</param>
    /// <param name="innerOutboxStore">The durable post-commit outbox store.</param>
    /// <param name="mode">
    /// The run's resolved mode. Without one the session folds as Balanced and assumes the run has a record, which never
    /// drops a completion.
    /// </param>
    public RuntimeCoalescingSession(
        string workflowExecutionId,
        IWorkflowSchedulerWorkQueue innerQueue,
        PersistenceModeOptions options,
        IRuntimePostCommitOutboxStore? innerOutboxStore = null,
        ResolvedPersistenceMode? mode = null)
        : this(workflowExecutionId, innerQueue, options, innerOutboxStore, mode, TimeProvider.System)
    {
    }

    internal RuntimeCoalescingSession(
        string workflowExecutionId,
        IWorkflowSchedulerWorkQueue innerQueue,
        PersistenceModeOptions options,
        IRuntimePostCommitOutboxStore? innerOutboxStore,
        ResolvedPersistenceMode? mode,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(innerQueue);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxActivitiesPerCommit <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxActivitiesPerCommit must be greater than zero.");

        mode ??= new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: true);
        if (!Enum.IsDefined(mode.Mode))
            throw new ArgumentException($"Persistence mode '{(int)mode.Mode}' is not supported.", nameof(mode));

        WorkflowExecutionId = workflowExecutionId;
        _innerQueue = innerQueue;
        _timeProvider = timeProvider;
        _innerOutboxStore = innerOutboxStore;
        Mode = mode.Mode;
        HasDurableRecord = mode.RunHasRecord;
        RecordCompletion = options.RecordCompletion;
        // Durable commits once per activity whatever the host's Balanced cap is. Ephemeral has no cap: it commits only at
        // boundaries, however many activities it folds.
        MaxActivitiesPerCommit = Mode switch
        {
            PersistenceMode.Durable => 1,
            PersistenceMode.Ephemeral => null,
            _ => options.MaxActivitiesPerCommit
        };
        CoalesceDurableValueReads = options.CoalesceDurableValueReads;
        _durableValuePageMemo = options.CoalesceDurableValueReads
            ? new RuntimeCoalescingDurableValuePageMemo()
            : null;
    }

    public string WorkflowExecutionId { get; }

    internal bool SupportsDeferredOverlayRelease =>
        _innerQueue.SupportsClaimTransitions && _innerQueue is IWorkflowSchedulerWorkVisibilityReader;

    /// <summary>The run's mode.</summary>
    public PersistenceMode Mode { get; }

    /// <summary>
    /// The most activities one segment folds before the next new activity forces an intermediate commit: one under
    /// Durable, the host's setting under Balanced, and <see langword="null"/> under Ephemeral, whose intermediate commits
    /// come only from boundaries, never from a count of activities.
    /// </summary>
    public int? MaxActivitiesPerCommit { get; }

    /// <summary>The host's <see cref="PersistenceModeOptions.RecordCompletion"/> setting.</summary>
    public bool RecordCompletion { get; }

    /// <summary>
    /// Whether the durable store holds a record of this run: it did when the drain began, or a commit through this
    /// session has landed since. Once true it stays true, so the run's completion is always committed.
    /// </summary>
    public bool HasDurableRecord { get; private set; }
    public bool CoalesceDurableValueReads { get; }
    internal RuntimeCoalescingDurableValuePageMemo? DurableValuePageMemo => _durableValuePageMemo;

    /// <summary>
    /// <see langword="true"/> while the session is coalescing. An activity-attempt boundary and the per-segment hop cap
    /// durably close one segment and start another in the same drain; terminal/external boundaries and quiescence
    /// deactivate the session.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>How many distinct activity executions the current segment's buffered checkpoints touched.</summary>
    public int ActivityCount => _segmentActivityIds.Count;
    public bool HasBufferedChanges => _bufferedChangeSets.Count > 0;

    public void Deactivate()
    {
        IsActive = false;
        DisableDurableValuePageReuse();
    }

    internal void BindDurableValuePageReuseCancellationToken(CancellationToken cancellationToken) =>
        _durableValuePageMemo?.BindOwnerCancellationToken(cancellationToken);

    /// <summary>Disables only the synchronized page memo; callers may use it from drain-cancellation callbacks.</summary>
    internal void DisableDurableValuePageReuse() => _durableValuePageMemo?.DisablePermanently();

    /// <summary>Returns <see langword="true"/> when this session owns the supplied workflow execution and is active.</summary>
    public bool AppliesTo(string workflowExecutionId) =>
        IsActive && StringComparer.Ordinal.Equals(WorkflowExecutionId, workflowExecutionId);

    /// <summary>Returns whether this exact claim was issued by the overlay queue.</summary>
    public bool OwnsOverlayClaim(RuntimeSchedulerWorkClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return _overlayClaims.ContainsKey(claim);
    }

    /// <summary>Requests a checkpoint flush before the next durable scheduler claim is acquired.</summary>
    internal void RequestDurableSchedulerClaimBoundary() => _durableSchedulerClaimBoundaryRequested = true;

    /// <summary>Consumes the one-shot request made when the overlay FIFO head requires a durable claim.</summary>
    internal bool ConsumeDurableSchedulerClaimBoundaryRequest()
    {
        var requested = _durableSchedulerClaimBoundaryRequested;
        _durableSchedulerClaimBoundaryRequested = false;
        return requested;
    }

    // ---- Buffering -------------------------------------------------------------------------------------------------

    /// <summary>Applies a deferred commit to the overlay working set and buffers its change-set for the eventual fold.</summary>
    public void BufferDeferred(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        ApplyToOverlay(commit.StateChanges, includeOutbox: true);
        _bufferedChangeSets.Add(commit.StateChanges);
        foreach (var activityExecutionId in ActivityExecutionIdsOf(commit))
            _segmentActivityIds.Add(activityExecutionId);
    }

    /// <summary>
    /// Whether buffering <paramref name="commit"/> would make the segment hold more than
    /// <see cref="MaxActivitiesPerCommit"/> activities. A checkpoint of an activity the segment already holds never trips
    /// the cap, so the cap counts activities, not the several checkpoints each activity produces. An empty segment
    /// always takes the commit, even one that touches more activities than the cap: a single commit is never split, and
    /// the intermediate commit exists to flush work already buffered, not to write one checkpoint on its own.
    /// </summary>
    public bool WouldExceedActivityCap(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (MaxActivitiesPerCommit is not { } cap || !HasBufferedChanges)
            return false;

        var added = ActivityExecutionIdsOf(commit).Count(id => !_segmentActivityIds.Contains(id));
        return ActivityCount + added > cap;
    }

    /// <summary>Records that a commit for this run reached the durable store.</summary>
    internal void MarkDurableRecord() => HasDurableRecord = true;

    /// <summary>
    /// Whether <paramref name="completion"/> may be dropped instead of committed: the one exception to committing a
    /// completion. It holds only for an Ephemeral run on a host with <see cref="RecordCompletion"/> off that has never
    /// been committed, and only when nothing in the folded segment outlives the run: no bookmark, incident, dispatch or
    /// dispatch cancellation, alteration outcome, operational change, acknowledged queue item, and no outbox item still
    /// waiting for delivery (a parent to resume, an event to publish). Anything else is committed as usual.
    /// </summary>
    public bool CanDiscardCompletion(
        RuntimeCheckpointCommit completion,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> remainingPendingOutbox)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(remainingPendingOutbox);

        if (Mode != PersistenceMode.Ephemeral || RecordCompletion || HasDurableRecord)
            return false;

        if (!StringComparer.Ordinal.Equals(completion.Checkpoint.Name, RuntimeCheckpointNames.WorkflowCompleted) ||
            completion.StateChanges.WorkflowExecution?.State.Status != WorkflowExecutionStatus.Completed)
            return false;

        if (remainingPendingOutbox.Count > 0 || completion.StateChanges.PostCommitOutbox.Count > 0 || _durablyPersistedOutboxIds.Count > 0)
            return false;

        var folded = FoldBufferedStateChangesWith(completion.StateChanges);
        return folded.Bookmarks.Count == 0 &&
               folded.Incidents.Count == 0 &&
               folded.Operational.Count == 0 &&
               folded.WorkflowDispatches.Count == 0 &&
               folded.WorkflowDispatchCancellations.Count == 0 &&
               folded.AlterationJobTerminalChange is null &&
               folded.ConsumedSchedulerWorkItems.Count == 0;
    }

    // An activity can appear on the checkpoint, in the activity states the commit writes, or both.
    private static IEnumerable<string> ActivityExecutionIdsOf(RuntimeCheckpointCommit commit) =>
        commit.Checkpoint.ActivityExecutionIds
            .Concat(commit.StateChanges.ActivityExecutions.Select(change => change.StateId))
            .Distinct(StringComparer.Ordinal);

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
        {
            _durablyPersistedOutboxIds.Add(change.StateId);
            if (StringComparer.Ordinal.Equals(change.State.Intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
                _mayReceiveDurableSchedulerDeliveries = true;
        }
    }

    /// <summary>Whether a mid-drain flush already durably persisted this outbox item.</summary>
    public bool IsOutboxDurablyPersisted(string outboxItemId) => _durablyPersistedOutboxIds.Contains(outboxItemId);

    internal void RegisterFusedScheduleAnchor(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (workItem.CommandKind != WorkflowExecutionCommandKind.ScheduleActivity)
            throw new ArgumentException("A fused-span anchor must be an original ScheduleActivity work item.", nameof(workItem));
        if (!StringComparer.Ordinal.Equals(workItem.WorkflowExecutionId, WorkflowExecutionId))
            throw new ArgumentException("A fused-span anchor must belong to this coalescing session.", nameof(workItem));

        _knownOverlayWorkItemIds.Add(workItem.WorkItemId);
        if (_activeFusedScheduleAnchors.TryAdd(workItem.WorkItemId, workItem))
            _activeFusedScheduleAnchorOrder.Add(workItem.WorkItemId);
    }

    internal void CompleteFusedScheduleAnchor(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        _activeFusedScheduleAnchors.Remove(workItemId);
        _activeFusedScheduleAnchorOrder.Remove(workItemId);
    }

    /// <summary>
    /// Persists original fused Schedule items before a continuing checkpoint can advance durable runtime state.
    /// Registration itself is memory-only; this method is called only at cap-fold and continuing attempt boundaries.
    /// </summary>
    internal async ValueTask EnsureActiveFusedScheduleAnchorsAsync(CancellationToken cancellationToken)
    {
        if (_activeFusedScheduleAnchorOrder.Count == 0)
            return;

        await EnsureQueueSeededAsync(cancellationToken);
        var durableIds = CreateDurableWorkItemIdSet();
        var enqueuedAnchor = false;
        foreach (var workItemId in _activeFusedScheduleAnchorOrder)
        {
            if (!durableIds.Add(workItemId))
                continue;

            var anchor = _activeFusedScheduleAnchors[workItemId];
            var persisted = await _innerQueue.EnqueueAsync(anchor, cancellationToken);
            if (!StringComparer.Ordinal.Equals(persisted.WorkflowExecutionId, WorkflowExecutionId) ||
                !StringComparer.Ordinal.Equals(persisted.WorkItemId, anchor.WorkItemId) ||
                persisted.CommandKind != WorkflowExecutionCommandKind.ScheduleActivity)
            {
                throw new InvalidOperationException(
                    $"The durable scheduler queue returned a conflicting item while persisting fused Schedule anchor '{anchor.WorkItemId}'.");
            }

            enqueuedAnchor = true;
        }

        if (enqueuedAnchor)
            await RefreshTrackedDurableWorkItemsAsync(durableIds, cancellationToken);
    }

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

    /// <summary>
    /// Returns the inspection projection this session last recorded for an activity execution, which is what a commit
    /// of every checkpoint would have stored by now.
    /// </summary>
    public bool TryGetInspection(string activityExecutionId, out ActivityExecutionInspectionProjection? projection)
    {
        var found = _inspectionUpserts.TryGetValue(activityExecutionId, out var recorded);
        projection = recorded;
        return found;
    }

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

    /// <summary>Renews a claim this overlay owns by the same rule every durable store applies.</summary>
    public RuntimePostCommitOutboxClaim? RenewOutboxClaim(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!_outboxItems.TryGetValue(claim.OutboxItemId, out var existing))
            return null;

        var renewed = RuntimePostCommitOutboxClaimTransitions.Renew(existing, claim, now, visibilityTimeout);
        if (renewed is not null)
            _outboxItems[claim.OutboxItemId] = renewed.Item;
        return renewed;
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

    /// <summary>
    /// Completes a claim this overlay owns by the same rule every durable store applies
    /// (<see cref="RuntimePostCommitOutboxClaimTransitions.ResolveCompletion"/>), and reports the outcome it applied.
    /// </summary>
    /// <param name="childExecution">
    /// The state of the child a projected dispatch names. A child is never this session's own execution, so the caller reads it
    /// from the state store.
    /// </param>
    public RuntimePostCommitOutboxClaimCompletionOutcome CompleteOutboxClaim(
        RuntimePostCommitOutboxClaimCompletion completion,
        WorkflowExecutionState? childExecution)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!_outboxItems.TryGetValue(completion.Claim.OutboxItemId, out var existingOutbox))
            throw new InvalidOperationException($"Post-commit outbox item '{completion.Claim.OutboxItemId}' was not found in the coalescing session.");
        WorkflowDispatchRecord? existingDispatch = null;
        if (completion.WorkflowDispatch is { } dispatch &&
            !_workflowDispatchUpserts.TryGetValue(dispatch.DispatchId, out existingDispatch))
        {
            throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found in the coalescing session.");
        }

        var resolution = RuntimePostCommitOutboxClaimTransitions.ResolveCompletion(
            completion,
            existingOutbox,
            existingDispatch,
            childExecution);
        if (resolution.FollowUpOutboxItem is { } followUp &&
            _outboxItems.TryGetValue(followUp.OutboxItemId, out var existingFollowUp) &&
            !existingFollowUp.IsEquivalentPendingItem(followUp))
        {
            throw new InvalidOperationException($"Post-commit follow-up item '{followUp.OutboxItemId}' already exists with conflicting state.");
        }

        _outboxItems[completion.Claim.OutboxItemId] = resolution.OutboxItem;
        if (resolution.WorkflowDispatch is { } workflowDispatch)
            _workflowDispatchUpserts[workflowDispatch.DispatchId] = workflowDispatch;
        if (resolution.FollowUpOutboxItem is { } followUpOutboxItem)
        {
            if (_outboxItems.TryAdd(followUpOutboxItem.OutboxItemId, followUpOutboxItem))
                _outboxOrder.Add(followUpOutboxItem.OutboxItemId);
        }
        return resolution.Outcome;
    }

    // ---- Queue overlay ---------------------------------------------------------------------------------------------

    /// <summary>Copies the durable inner queue's current items for this workflow execution into the overlay once.</summary>
    public async ValueTask EnsureQueueSeededAsync(CancellationToken cancellationToken)
    {
        if (_queueSeeded)
            return;

        _durableWorkItemIds.Clear();
        if (SupportsDeferredOverlayRelease)
        {
            var reader = (IWorkflowSchedulerWorkVisibilityReader)_innerQueue;
            foreach (var entry in await ReadVisibilityAsync(reader, cancellationToken))
            {
                await SeedAsync(entry.Item);
                _durableVisibility.Add(entry.Item.WorkItemId, entry.VisibleAfter);
            }
        }
        else
        {
            foreach (var item in await _innerQueue.ListAllAsync(WorkflowExecutionId, cancellationToken))
                await SeedAsync(item);
        }

        async ValueTask SeedAsync(RuntimeSchedulerWorkItem item)
        {
            await _overlayQueue.EnqueueAsync(item, cancellationToken);
            _durableWorkItemIds.Add(item.WorkItemId);
            _knownOverlayWorkItemIds.Add(item.WorkItemId);
        }

        _queueSeeded = true;
    }

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueOverlayAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        await EnsureQueueSeededAsync(cancellationToken);
        var queued = await _overlayQueue.EnqueueAsync(workItem, cancellationToken);
        _knownOverlayWorkItemIds.Add(queued.WorkItemId);
        return queued;
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
        // Item-only queue pages omit active-claim and retry visibility. Preserve it from the same provider read
        // that seeded the overlay; a new drain must not turn a delayed durable resume into immediately runnable work.
        var heads = await _overlayQueue.ListNextWorkItemsAsync([WorkflowExecutionId], cancellationToken);
        var head = heads.GetValueOrDefault(WorkflowExecutionId);
        if (head is not null && _durableVisibility.TryGetValue(head.WorkItemId, out var visibleAfter) &&
            visibleAfter > request.Now)
            return null;

        var claim = await _overlayQueue.ClaimAsync(request, cancellationToken);
        if (claim is not null)
            _overlayClaims.TryAdd(claim, request.VisibilityTimeout);
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
            _overlayClaims.TryAdd(result.Claim, visibilityTimeout);
        return result;
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteOverlayClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken)
    {
        var result = await _overlayQueue.CompleteClaimAsync(claim, cancellationToken);
        _overlayClaims.TryRemove(claim, out _);
        if (result.Succeeded && !IsActive && _durableWorkItemIds.Contains(claim.Item.WorkItemId))
        {
            // The final decision already passed the execution fence. A retaining boundary may have ended
            // coalescing before that decision, so no later active flush will acknowledge this durable copy.
            // As in AdvanceInnerQueueAsync, clean only its exact accepted identity, never a sibling/head.
            await _innerQueue.DeleteAsync(WorkflowExecutionId, claim.Item.WorkItemId, cancellationToken);
            _durableWorkItemIds.Remove(claim.Item.WorkItemId);
            _durablyConsumedWorkItemIds.Add(claim.Item.WorkItemId);
        }
        return result;
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseOverlayClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken)
    {
        if (SupportsDeferredOverlayRelease && RuntimeUnavailableWork.SupportsDelayedRelease(claim.Item.CommandKind) &&
            _durablyConsumedWorkItemIds.Contains(claim.Item.WorkItemId))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var result = await _overlayQueue.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
        var owned = _overlayClaims.TryRemove(claim, out var visibilityTimeout);
        if (result.Succeeded && owned && SupportsDeferredOverlayRelease &&
            RuntimeUnavailableWork.SupportsDelayedRelease(claim.Item.CommandKind))
        {
            _pendingReleases[claim.Item.WorkItemId] = new PendingRelease(claim, visibleAt, visibilityTimeout);
            // A handler can cross a durable boundary before reporting unavailable. If it deactivated the overlay,
            // its source must still exist; persist the release now rather than waiting for an inactive scope's flush.
            if (!IsActive)
                await PersistPendingReleasesAsync(cancellationToken);
        }
        return result;
    }

    // ---- Inline fused pump ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the FIFO-earliest overlay item not currently claimed in flight. The drain loop's claim on
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
    /// Removes an inline-pumped item from the overlay after its dispatch completed. Flush accounting
    /// removes that exact durable identity only after its effect has committed, preserving active recovery anchors.
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
        _segmentActivityIds.Clear();
    }

    /// <summary>
    /// Advances the durable inner queue to reflect the coalesced segment: deletes only known consumed identities
    /// and durably enqueues any remaining unconsumed continuation. Called as part of a flush, after the folded
    /// checkpoint commit has landed durably (condition B).
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
    public ValueTask AdvanceInnerQueueAsync(bool consumeInFlightClaims, CancellationToken cancellationToken) =>
        AdvanceInnerQueueAsync(consumeInFlightClaims, cancellationToken, preserveActiveFusedScheduleAnchors: false);

    internal async ValueTask AdvanceInnerQueueAsync(
        bool consumeInFlightClaims,
        CancellationToken cancellationToken,
        bool preserveActiveFusedScheduleAnchors)
    {
        if (!_queueSeeded)
            return;

        IReadOnlyCollection<RuntimeSchedulerWorkItem> remaining =
            (await ReadVisibilityAsync(_overlayQueue, cancellationToken)).Select(entry => entry.Item).ToArray();

        if (consumeInFlightClaims && _overlayClaims.Count > 0)
        {
            var inFlight = new HashSet<string>(
                _overlayClaims.Keys.Select(claim => claim.Item.WorkItemId),
                StringComparer.Ordinal);
            remaining = remaining.Where(item => !inFlight.Contains(item.WorkItemId)).ToArray();
        }

        var remainingIds = new HashSet<string>(remaining.Select(item => item.WorkItemId), StringComparer.Ordinal);
        if (preserveActiveFusedScheduleAnchors)
            remainingIds.UnionWith(_activeFusedScheduleAnchorOrder);

        var expectedDurableIds = CreateDurableWorkItemIdSet();
        var enqueuedContinuation = false;
        foreach (var item in remaining)
        {
            if (!expectedDurableIds.Add(item.WorkItemId))
                continue;

            var persisted = await _innerQueue.EnqueueAsync(item, cancellationToken);
            if (!StringComparer.Ordinal.Equals(persisted.WorkflowExecutionId, WorkflowExecutionId) ||
                !StringComparer.Ordinal.Equals(persisted.WorkItemId, item.WorkItemId) ||
                persisted.CommandKind != item.CommandKind)
            {
                throw new InvalidOperationException(
                    $"The durable scheduler queue returned a conflicting item while advancing continuation '{item.WorkItemId}'.");
            }

            enqueuedContinuation = true;
        }

        // A separate outbox deliverer can add durable rows while this drain owns the execution. Refresh after inserts
        // to recognize its deliveries of work this overlay already knows; unrelated additions remain untouched.
        // Keep refreshing for the rest of a drain that persisted scheduler intents: a peer may own their fenced
        // delivery even after this overlay records its local outcome. A boundary with no new insert still has to
        // recognize already-arrived deliveries. This is not an exactly-once guarantee for a later peer retry.
        if (enqueuedContinuation || _mayReceiveDurableSchedulerDeliveries)
            await RefreshTrackedDurableWorkItemsAsync(expectedDurableIds, cancellationToken);

        // Listing order is not a dequeue contract, and an outbox enqueue can change the head between a read and a
        // dequeue. Identity deletion cannot remove that new work or an unconsumed anchor behind a consumed item.
        foreach (var workItemId in _durableWorkItemIds.Where(id => !remainingIds.Contains(id)).ToArray())
        {
            // False means already absent. The effect is committed and this identity is consumed, so cleanup is
            // idempotent; unlike Dequeue, either result leaves every other queued identity untouched.
            await _innerQueue.DeleteAsync(WorkflowExecutionId, workItemId, cancellationToken);
            _durableWorkItemIds.Remove(workItemId);
            _durablyConsumedWorkItemIds.Add(workItemId);
        }

        await PersistPendingReleasesAsync(cancellationToken);
    }

    private async ValueTask<IReadOnlyList<RuntimeSchedulerWorkVisibility>> ReadVisibilityAsync(
        IWorkflowSchedulerWorkVisibilityReader reader,
        CancellationToken cancellationToken)
    {
        var entries = new List<RuntimeSchedulerWorkVisibility>();
        string? continuation = null;
        do
        {
            var page = await reader.ListWithVisibilityAsync(
                new RuntimeSchedulerWorkQuery(WorkflowExecutionId, continuationToken: continuation), cancellationToken);
            entries.AddRange(page.Items);
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return entries;
    }

    private async ValueTask PersistPendingReleasesAsync(CancellationToken cancellationToken)
    {
        foreach (var (workItemId, pending) in _pendingReleases.ToArray())
        {
            // The execution lease still owns this drain. Reconciliation has removed the consumed prefix, so the
            // released item must now be the durable FIFO head. Never apply its delay to a different queued item.
            var now = _timeProvider.GetUtcNow();
            var claim = await _innerQueue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                WorkflowExecutionId, pending.Claim.OwnerId, now,
                pending.VisibilityTimeout), cancellationToken);
            if (claim is null)
                throw new InvalidOperationException("The deferred scheduler work could not be claimed durably.");
            if (!StringComparer.Ordinal.Equals(claim.Item.WorkItemId, workItemId))
            {
                await _innerQueue.ReleaseClaimAsync(claim, now, cancellationToken);
                throw new InvalidOperationException("The durable scheduler head changed before its deferred release.");
            }
            var released = await _innerQueue.ReleaseClaimAsync(claim, pending.VisibleAt, cancellationToken);
            if (released.Status != RuntimeSchedulerWorkClaimTransitionStatus.Succeeded)
                throw new InvalidOperationException("The deferred scheduler work lost its durable claim before release.");
            _pendingReleases.Remove(workItemId);
        }
    }

    private sealed record PendingRelease(RuntimeSchedulerWorkClaim Claim, DateTimeOffset VisibleAt, TimeSpan VisibilityTimeout);

    private HashSet<string> CreateDurableWorkItemIdSet()
    {
        var ids = new HashSet<string>(_durableWorkItemIds, StringComparer.Ordinal);
        if (ids.Count != _durableWorkItemIds.Count)
            throw new InvalidOperationException("The tracked durable scheduler queue contains duplicate work-item identities.");
        return ids;
    }

    private async ValueTask RefreshTrackedDurableWorkItemsAsync(
        IReadOnlySet<string> expectedIds,
        CancellationToken cancellationToken)
    {
        var actualItems = await _innerQueue.ListAllAsync(WorkflowExecutionId, cancellationToken);
        var actualIds = actualItems.Select(item => item.WorkItemId).ToArray();
        var actualIdSet = new HashSet<string>(actualIds, StringComparer.Ordinal);
        if (actualIdSet.Count != actualIds.Length || !expectedIds.IsSubsetOf(actualIdSet))
        {
            throw new InvalidOperationException(
                "The durable scheduler queue lost tracked work or returned duplicate identities during coalescing reconciliation.");
        }

        _durableWorkItemIds.Clear();
        _durableWorkItemIds.AddRange(actualIds.Where(_knownOverlayWorkItemIds.Contains));
    }
}
