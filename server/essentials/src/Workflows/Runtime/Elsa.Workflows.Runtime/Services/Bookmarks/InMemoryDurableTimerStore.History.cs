using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

public sealed partial class InMemoryDurableTimerStore
{
    private readonly IPersistenceAccessContextAccessor? _access;
    private readonly InMemoryRuntimeCheckpointStoreState? _checkpoints;
    private readonly InMemoryWorkflowExecutionStateStore? _executions;
    private readonly InMemoryExecutionLivenessStateStore? _liveness;
    private readonly TimeProvider _clock = TimeProvider.System;

    // Composition remains internal until the timer pump, source qualification and no-record owner are complete.
    internal InMemoryDurableTimerStore(InMemoryRuntimeCheckpointStoreState checkpoints, IPersistenceAccessContextAccessor access,
        IWorkflowExecutionStateStore executions, IExecutionLivenessStateStore liveness, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(checkpoints); ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(executions); ArgumentNullException.ThrowIfNull(liveness); ArgumentNullException.ThrowIfNull(clock);
        _checkpoints = checkpoints; _shared = checkpoints.TimerSources; _access = access;
        _executions = executions as InMemoryWorkflowExecutionStateStore;
        _liveness = liveness as InMemoryExecutionLivenessStateStore; _clock = clock;
    }

    public bool SupportsHistoryResponsibility
    {
        get
        {
            try
            {
                return _access?.Current.RequireScope() is not null && _checkpoints is not null && _executions is not null &&
                    _liveness is not null && ReferenceEquals(_executions.CheckpointState, _checkpoints) &&
                    ReferenceEquals(_executions.ExecutionLivenessStateStore, _liveness);
            }
            catch (InvalidOperationException) { return false; }
        }
    }

    public bool CanInspectHistorySources(IRuntimeHistoryStore history)
    {
        if (!SupportsHistoryResponsibility || history is not InMemoryRuntimeHistoryStore selected) return false;
        try { return selected.Uses(_checkpoints!, _access!.Current.RequireScope()) && selected.UsesAccessor(_access); }
        catch (InvalidOperationException) { return false; }
    }

    private string Scope => _access is null ? PersistenceScope.DefaultValue : _access.Current.RequireScope().Value;
    private TimerBucket Bucket
    {
        get
        {
            if (!_shared.Scopes.TryGetValue(Scope, out var bucket)) _shared.Scopes[Scope] = bucket = new();
            if (bucket.RequiresHistory && !SupportsHistoryResponsibility)
                throw new NotSupportedException("This timer adapter cannot read captured source responsibility.");
            return bucket;
        }
    }

    private async ValueTask<DurableTimer> SaveLegacyAsync(DurableTimer timer, CancellationToken cancellationToken)
    {
        var context = RequireHistory();
        return await _checkpoints!.ExecuteWriteGateAsync(async token =>
        {
            EnsureAccess(context);
            lock (_syncRoot)
                if (_timers.TryGetValue(new(timer.WorkflowExecutionId, timer.TimerId), out var winner)) return winner.Timer;
            var native = await _executions!.FindAsync(timer.WorkflowExecutionId, token);
            if (await _executions.IsDeletionTombstonedAsync(timer.WorkflowExecutionId, token))
                throw Unavailable("A deleted workflow cannot admit a new timer source.");
            EnsureAccess(context);
            if (native is not null) EnsureNativeScope(context, native);
            lock (_syncRoot)
            {
                if (_shared.SourceOwners.ContainsKey(timer.WorkflowExecutionId) || native is { HistoryCaptureEnabledAtAdmission: not false, HistoryCapturePolicy: { } policy } &&
                    policy.SupportedKinds.Any(kind => DurableTimerHistoryFacts.Kinds.Contains(kind, StringComparer.Ordinal)))
                    throw Unavailable("A timer under the original captured choice must retain its scheduling responsibility.");
                var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);
                _timers.Add(key, new(timer)); AddRunKey(key); return timer;
            }
        }, cancellationToken);
    }

    private async ValueTask<DurableTimer> SaveCapturedAsync(DurableTimer timer, CancellationToken cancellationToken)
    {
        var context = RequireHistory();
        // The native checkpoint/deletion gate precedes liveness and source locks, including the first read.
        return await _checkpoints!.ExecuteWriteGateAsync(token => _liveness!.ExecuteOwnershipAtomicAsync(async inner =>
        {
            EnsureAccess(context);
            lock (_syncRoot)
                if (_timers.TryGetValue(new(timer.WorkflowExecutionId, timer.TimerId), out var winner)) return winner.Timer;
            var history = timer.HistoryResponsibility!;
            if (!DurableTimerHistoryFacts.Kinds.All(kind => history.Policy.SupportedKinds.Contains(kind, StringComparer.Ordinal)))
                throw new NotSupportedException("A new timer source requires its complete original schedule and terminal vocabulary.");
            if (history.Retired || history.NoRecordCompletion is not null || history.PendingEvents.Count != 1 ||
                history.PendingEvents[0].Name != "TimerScheduled")
                throw new InvalidOperationException("A new captured timer owes its original schedule and cannot start retired or acknowledged.");
            ValidateSourceObservation(timer, new(history.Policy, history.PendingEvents[0]), terminal: false);
            var native = await _executions!.FindAsync(timer.WorkflowExecutionId, inner);
            var ownership = await _liveness.FindAsync(timer.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(timer.WorkflowExecutionId), inner);
            EnsureAccess(context);
            if (ownership?.Metadata.GetValueOrDefault(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone) is { } tombstone && tombstone != "false")
                throw Unavailable("A deleted workflow cannot admit a new timer source.");
            if (ownership?.ExecutionLease is not { FencingToken: > 0 } lease || lease.IsExpired(_clock.GetUtcNow()))
                throw Unavailable("A new captured timer requires its current live execution owner.");
            if (native is not null)
            {
                EnsureNativeScope(context, native);
                if (native.Status.IsTerminal() || native.HistoryCaptureEnabledAtAdmission == false ||
                    !history.Policy.IsEquivalentTo(native.HistoryCapturePolicy))
                    throw Unavailable("The native run cannot admit a new timer under this capture choice.");
            }
            lock (_checkpoints.SyncRoot)
            {
                var key = (context.RequireScope().Value, timer.WorkflowExecutionId);
                if (_checkpoints.HistoryDeletedRuns.Contains(key)) throw Unavailable("The timer's native run has been deleted.");
                if (_checkpoints.HistoryRunOwners.TryGetValue(timer.WorkflowExecutionId, out var owner) &&
                    (owner.Scope != key.Item1 || !history.Policy.IsEquivalentTo(owner.Policy)))
                    throw new RuntimeHistoryIntegrityException(timer.WorkflowExecutionId, "timer admission disagrees with the native history owner");
                if (native is null && (_checkpoints.HistoryHeads.ContainsKey(key) ||
                    _checkpoints.HistoryRunOwners.ContainsKey(timer.WorkflowExecutionId)))
                    throw new RuntimeHistoryIntegrityException(timer.WorkflowExecutionId, "timer admission has history without its native run");
            }
            inner.ThrowIfCancellationRequested(); EnsureAccess(context);
            lock (_syncRoot)
            {
                var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);
                if (_timers.TryGetValue(key, out var winner)) return winner.Timer;
                if (native is null && _shared.NoRecordClosures.ContainsKey(timer.WorkflowExecutionId))
                    throw Unavailable("A completed unrecorded source cannot admit a new timer without native recovery authority.");
                if (_shared.SourceOwners.TryGetValue(timer.WorkflowExecutionId, out var sourceOwner) &&
                    (sourceOwner.Scope != Scope || !history.Policy.IsEquivalentTo(sourceOwner.Policy)))
                    throw new RuntimeHistoryIntegrityException(timer.WorkflowExecutionId, "timer source ownership differs from its original scope or policy");
                _shared.SourceOwners[timer.WorkflowExecutionId] = (Scope, history.Policy);
                _timers.Add(key, new(timer)); AddRunKey(key); Bucket.RequiresHistory = true;
                Bucket.ActiveCaptured[timer.WorkflowExecutionId] = checked(Bucket.ActiveCaptured.GetValueOrDefault(timer.WorkflowExecutionId) + 1);
                UpdatePending(key);
                return timer;
            }
        }, token), cancellationToken);
    }

    public ValueTask<bool> HasPendingHistoryAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); cancellationToken.ThrowIfCancellationRequested(); RequireHistory();
        lock (_syncRoot) return ValueTask.FromResult(Bucket.PendingRuns.GetValueOrDefault(workflowExecutionId) > 0);
    }

    public ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); cancellationToken.ThrowIfCancellationRequested(); RequireHistory();
        var binding = RuntimeHistoryIdentity.CreateBinding(["timer-pending-v1", Scope, query.Limit.ToString(CultureInfo.InvariantCulture)]);
        var after = InMemoryRuntimeStoreContinuation.Decode(query.ContinuationToken, binding, nameof(query));
        lock (_syncRoot)
        {
            var index = Bucket.Pending; var low = 0; var high = index.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (after is not null && StringComparer.Ordinal.Compare(index.Keys[middle], after) <= 0) low = middle + 1;
                else high = middle;
            }
            var items = new List<DurableTimer>(query.Limit); string? last = null;
            while (low < index.Count && items.Count < query.Limit)
            { last = index.Keys[low]; items.Add(_timers[index.Values[low++]].Timer); }
            return ValueTask.FromResult(new RuntimeStorePage<DurableTimer>(query, items,
                low < index.Count ? InMemoryRuntimeStoreContinuation.Encode(binding, last!) : null));
        }
    }

    public async ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId); cancellationToken.ThrowIfCancellationRequested();
        var context = RequireHistory();
        return await _checkpoints!.ExecuteWriteGateAsync(async token =>
        {
            EnsureAccess(context);
            var native = await _executions!.FindAsync(workflowExecutionId, token); EnsureAccess(context);
            if (native is null) return false;
            EnsureNativeScope(context, native);
            lock (_syncRoot)
            {
                var key = new TimerKey(workflowExecutionId, timerId);
                if (!_timers.TryGetValue(key, out var state)) return true;
                var history = state.Timer.HistoryResponsibility ?? throw new InvalidOperationException("A legacy timer has no history responsibility.");
                var owed = history.PendingEvents.SingleOrDefault(e => e.HistoryEventId == historyEventId);
                if (owed is null) return true;
                lock (_checkpoints.SyncRoot)
                {
                    var ownerKey = (context.RequireScope().Value, workflowExecutionId);
                    if (_checkpoints.HistoryDeletedRuns.Contains(ownerKey)) return false;
                    if (!_checkpoints.HistoryHeads.TryGetValue(ownerKey, out var head)) return false;
                    InMemoryRuntimeHistoryStore.ValidateHead(workflowExecutionId, head);
                    if (!_checkpoints.HistoryRunOwners.TryGetValue(workflowExecutionId, out var owner) || owner.Scope != ownerKey.Item1 ||
                        !history.Policy.IsEquivalentTo(owner.Policy) || !history.Policy.IsEquivalentTo(native.HistoryCapturePolicy) ||
                        !history.Policy.IsEquivalentTo(head.Policy) || native.HistoryCaptureEnabledAtAdmission == false)
                        throw new RuntimeHistoryIntegrityException(workflowExecutionId, "timer acknowledgement differs from the retained native policy");
                    var receipt = (ownerKey.Item1, workflowExecutionId, historyEventId);
                    if (!_checkpoints.HistoryCandidateFingerprints.TryGetValue(receipt, out var fingerprint)) return false;
                    if (fingerprint != "observation:" + RuntimeHistoryIdentity.Compute(owed) ||
                        !_checkpoints.HistoryReceiptPositions.TryGetValue(receipt, out var position) || position <= 0 || position > head.LastSequence)
                        throw new RuntimeHistoryIntegrityException(workflowExecutionId, "timer acknowledgement has an invalid semantic receipt");
                    var stored = _checkpoints.HistoryEventsById.GetValueOrDefault(receipt);
                    if (position <= head.PurgedThroughSequence ? stored is not null : position < head.FirstSequence || stored is null || stored.Sequence != position ||
                        stored.RecordedAt is null || stored.PhysicalCommitId != "owner-observation:" + historyEventId ||
                        "observation:" + RuntimeHistoryIdentity.Compute(stored) != fingerprint)
                        throw new RuntimeHistoryIntegrityException(workflowExecutionId, "timer acknowledgement has invalid retained event content");
                }
                state.Timer = state.Timer with
                {
                    HistoryResponsibility = new(history.Policy, history.PendingEvents.Where(e => e.HistoryEventId != historyEventId).ToArray(), history.Retired)
                };
                if (state.Timer.HistoryResponsibility is { Retired: true, PendingEvents.Count: 0 })
                    state.Timer = state.Timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null };
                // Observation acknowledgement must not invalidate a live operational timer claim.
                UpdatePending(key); return true;
            }
        }, cancellationToken);
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim,
        RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested(); RequireHistory();
        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_timers.TryGetValue(key, out var state) || IsRetired(state)) return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out state)) return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Stale);
            Retire(key, state, observation); return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Applied());
        }
    }

    public ValueTask RetireWithHistoryAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        ArgumentNullException.ThrowIfNull(observation); cancellationToken.ThrowIfCancellationRequested(); RequireHistory();
        lock (_syncRoot)
        {
            var key = new TimerKey(workflowExecutionId, timerId);
            if (_timers.TryGetValue(key, out var state) && !IsRetired(state)) Retire(key, state, observation);
        }
        return ValueTask.CompletedTask;
    }

    private void Retire(TimerKey key, TimerState state, RuntimeHistoryObservation observation)
    {
        RequireHistory(); ValidateSourceObservation(state.Timer, observation, terminal: true);
        var history = state.Timer.HistoryResponsibility ?? throw new InvalidOperationException("A legacy timer cannot acquire history retroactively.");
        var next = new DurableTimerHistoryResponsibility(history.Policy, history.PendingEvents.Append(observation.Event).ToArray(), true);
        var revision = checked(state.Revision + 1);
        state.Timer = state.Timer with { HistoryResponsibility = next }; state.Revision = revision;
        var active = Bucket.ActiveCaptured[key.WorkflowExecutionId] - 1;
        if (active == 0) Bucket.ActiveCaptured.Remove(key.WorkflowExecutionId); else Bucket.ActiveCaptured[key.WorkflowExecutionId] = active;
        state.OwnerId = null; state.ClaimedAt = null; UpdatePending(key);
    }

    private static void ValidateSourceObservation(DurableTimer timer, RuntimeHistoryObservation observation, bool terminal)
    {
        RuntimeHistorySafety.ValidateObservation(observation);
        var e = observation.Event;
        if (timer.HistoryResponsibility is not { } history || !history.Policy.IsEquivalentTo(observation.Policy) ||
            (terminal ? e.Name is not ("TimerDispatched" or "TimerRetired") : e.Name != "TimerScheduled") ||
            e.WorkflowExecutionId != timer.WorkflowExecutionId || e.ActivityExecutionId != timer.ActivityExecutionId ||
            e.ExecutionScopeId != timer.ExecutionScopeId || e.Metadata["history.timerId"] != timer.TimerId ||
            e.Metadata["history.scheduledAt"] != timer.DueTime.ToString("O", CultureInfo.InvariantCulture) ||
            !terminal && e.OccurredAt != timer.CreatedAt)
            throw new InvalidOperationException("The timer observation differs from its exact original source.");
    }

    private PersistenceAccessContext RequireHistory()
    {
        if (!SupportsHistoryResponsibility) throw new NotSupportedException("Timer history requires a coherent native/liveness owner and one ordinary persistence scope.");
        return _access!.Current;
    }
    private void EnsureAccess(PersistenceAccessContext context)
    {
        if (_access!.Current != context) throw new InvalidOperationException("Persistence access changed during timer history admission.");
    }
    private static void EnsureNativeScope(PersistenceAccessContext context, WorkflowExecutionState native)
    {
        if (native.Partition is { } partition) context.EnsureScope(new(partition.Value));
        if (!NativeIngressCheckpointAuthority.HasBoundScope(native, context.RequireScope().Value)) context.EnsureTenantScope(native.TenantId);
    }
    private void EnsureReadable(TimerState state)
    {
        if (state.Timer.HistoryResponsibility is not null) RequireHistory();
    }
    private static bool IsRetired(TimerState state) => state.Timer.HistoryResponsibility?.Retired == true;
    private static string HistoryKey(TimerKey key) => RuntimeHistoryIdentity.CreateBinding([key.WorkflowExecutionId, key.TimerId]);
    private void UpdatePending(TimerKey key)
    {
        var order = HistoryKey(key);
        if (_timers[key].Timer.HistoryResponsibility?.PendingEvents.Count > 0)
        {
            if (Bucket.Pending.TryGetValue(order, out var existing))
            {
                if (existing != key) throw new InvalidOperationException("Timer pending responsibility has an identity collision.");
                return;
            }
            var count = checked(Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) + 1);
            if (!Bucket.PendingByRun.TryGetValue(key.WorkflowExecutionId, out var run)) Bucket.PendingByRun[key.WorkflowExecutionId] = run = new(StringComparer.Ordinal);
            run.Add(order, key);
            Bucket.Pending.Add(order, key); Bucket.PendingRuns[key.WorkflowExecutionId] = count;
        }
        else RemovePending(key);
    }
    private void RemovePending(TimerKey key)
    {
        RemovePending(Bucket, key);
    }
    private static void RemovePending(TimerBucket bucket, TimerKey key)
    {
        if (!bucket.Pending.Remove(HistoryKey(key))) return;
        var pending = bucket.PendingByRun[key.WorkflowExecutionId]; pending.Remove(HistoryKey(key));
        if (pending.Count == 0) bucket.PendingByRun.Remove(key.WorkflowExecutionId);
        var count = bucket.PendingRuns[key.WorkflowExecutionId] - 1;
        if (count == 0) bucket.PendingRuns.Remove(key.WorkflowExecutionId);
        else bucket.PendingRuns[key.WorkflowExecutionId] = count;
    }
    private void AddRunKey(TimerKey key)
    {
        if (!_shared.Runs.TryGetValue(key.WorkflowExecutionId, out var sources)) _shared.Runs[key.WorkflowExecutionId] = sources = [];
        sources.Add((Scope, key.TimerId));
    }
    private void RemoveRunKey(TimerKey key)
    {
        if (!_shared.Runs.TryGetValue(key.WorkflowExecutionId, out var sources)) return;
        sources.Remove((Scope, key.TimerId));
        if (sources.Count == 0) _shared.Runs.Remove(key.WorkflowExecutionId);
    }

    internal bool CanDeleteForWorkflowExecution(string run, InMemoryRuntimeCheckpointStoreState nativeOwner)
    {
        if (_checkpoints is not null) return ReferenceEquals(_checkpoints, nativeOwner);
        lock (nativeOwner.TimerSources.SyncRoot) return !nativeOwner.TimerSources.Runs.ContainsKey(run);
    }
    private static RuntimeSchedulerWorkUnavailableException Unavailable(string detail) => new("runtime.history.capture-unavailable", detail);

    internal sealed class SharedTimerState
    {
        internal object SyncRoot { get; } = new();
        internal Dictionary<string, (string Scope, RuntimeHistoryCapturePolicy Policy)> SourceOwners { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<(string Scope, string Timer)>> Runs { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> NoRecordCompletionRuns { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, DurableTimerNoRecordCompletion> NoRecordClosures { get; } = new(StringComparer.Ordinal);
        internal bool HasHistoryOwner(string run)
        {
            lock (SyncRoot) return SourceOwners.ContainsKey(run);
        }
        internal bool HasNoRecordCompletion(string scope, string run)
        {
            lock (SyncRoot) return NoRecordCompletionRuns.Contains(run) && SourceOwners.TryGetValue(run, out var owner) && owner.Scope == scope;
        }
        internal void ValidateHistoryOwner(string scope, string run, RuntimeHistoryCapturePolicy? policy)
        {
            lock (SyncRoot)
                if (SourceOwners.TryGetValue(run, out var owner) && (owner.Scope != scope || policy is null || !policy.IsEquivalentTo(owner.Policy)))
                    throw new RuntimeHistoryIntegrityException(run, "native history differs from its original timer source scope or capture policy");
        }
        internal void DeleteForWorkflowExecution(string run)
        {
            lock (SyncRoot)
            {
                // Native deletion owns the checkpoint/liveness gates. Its indexed run scopes
                // remain authoritative when maintenance access is global or selects another scope.
                if (Runs.Remove(run, out var sources))
                    foreach (var source in sources)
                    {
                        var bucket = Scopes[source.Scope]; var key = new TimerKey(run, source.Timer);
                        bucket.Timers.Remove(key); RemovePending(bucket, key);
                        bucket.ActiveCaptured.Remove(run);
                    }
                NoRecordCompletionRuns.Remove(run);
                NoRecordClosures.Remove(run);
                SourceOwners.Remove(run);
            }
        }
        private readonly Dictionary<string, TimerBucket> _scopes = new(StringComparer.Ordinal);
        internal Dictionary<string, TimerBucket> Scopes => _scopes;
    }
    internal sealed class TimerBucket
    {
        internal Dictionary<TimerKey, TimerState> Timers { get; } = new();
        internal SortedList<string, TimerKey> Pending { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> PendingRuns { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, SortedList<string, TimerKey>> PendingByRun { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> ActiveCaptured { get; } = new(StringComparer.Ordinal);
        internal bool RequiresHistory { get; set; }
    }
}
