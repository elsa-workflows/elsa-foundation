using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

/// <summary>
/// Process-local <see cref="IDurableTimerStore"/>. Timers are held in memory only, so a <c>Delay</c> backed
/// by this store is <b>not</b> restart-durable — a process restart forgets every pending timer. Compose a
/// durable persistence provider (e.g. the Runtime EF Core module) to make timers survive restarts.
/// </summary>
[RuntimeDefaultRegistration]
public sealed class InMemoryDurableTimerStore : IDurableTimerHistoryStore
{
    private readonly InMemoryDurableTimerStoreState _shared;
    private readonly IPersistenceAccessContextAccessor? _access;
    private readonly TimeProvider _clock;
    private readonly InMemoryRuntimeCheckpointStoreState? _checkpoints;
    private readonly InMemoryWorkflowExecutionStateStore? _nativeExecutions;
    private readonly InMemoryExecutionLivenessStateStore? _nativeLiveness;
    private object _syncRoot => _shared.SyncRoot;
    private string Scope => _access?.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
    private InMemoryDurableTimerStoreState.Bucket Bucket
    {
        get
        {
            if (!_shared.Scopes.TryGetValue(Scope, out var bucket)) _shared.Scopes[Scope] = bucket = new();
            return bucket;
        }
    }
    private Dictionary<TimerKey, TimerState> _timers => Bucket.Timers;
    public InMemoryDurableTimerStore() : this(new InMemoryDurableTimerStoreState(), null, null) { }
    public InMemoryDurableTimerStore(InMemoryDurableTimerStoreState state, IPersistenceAccessContextAccessor? access, TimeProvider? clock = null)
    { _shared = state; _access = access; _clock = clock ?? TimeProvider.System; }
    public InMemoryDurableTimerStore(InMemoryDurableTimerStoreState state, IPersistenceAccessContextAccessor? access, TimeProvider? clock,
        InMemoryRuntimeCheckpointStoreState? checkpoints, IWorkflowExecutionStateStore? executions, IExecutionLivenessStateStore? liveness)
        : this(state, access, clock)
    { _checkpoints = checkpoints; _nativeExecutions = executions as InMemoryWorkflowExecutionStateStore; _nativeLiveness = liveness as InMemoryExecutionLivenessStateStore; }
    public bool SupportsHistoryResponsibility
    {
        get { try { return _access?.Current.RequireScope() is not null && _checkpoints is not null && _nativeExecutions is not null &&
            ReferenceEquals(_nativeExecutions.CheckpointState, _checkpoints); } catch (InvalidOperationException) { return false; } }
    }


    internal void DeleteForWorkflowExecution(string workflowExecutionId)
    {
        lock (_syncRoot)
        {
            foreach (var key in (Bucket.Runs.GetValueOrDefault(workflowExecutionId) ?? []).ToArray())
            {
                _timers.Remove(key);
                Bucket.Pending.Remove(HistoryKey(key));
            }
            Bucket.Runs.Remove(workflowExecutionId); Bucket.PendingByRun.Remove(workflowExecutionId);
            Bucket.PendingRuns.Remove(workflowExecutionId); Bucket.ActiveCaptured.Remove(workflowExecutionId);
            if (_checkpoints is not null) lock (_checkpoints.SyncRoot) _checkpoints.NoRecordTimerCompletionRuns.Remove((Scope, workflowExecutionId));
        }
    }

    public bool SupportsClaimTransitions => true;

    public ValueTask<DateTimeOffset?> GetNextDueTimeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return ValueTask.FromResult(_timers.Values
                .Where(state => !IsRetired(state) && CanRead(state))
                .Select(state => (DateTimeOffset?)(state.VisibleAfter > state.Timer.DueTime
                    ? state.VisibleAfter.Value : state.Timer.DueTime))
                .Min());
        }
    }

    public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();
        return timer.HistoryResponsibility is null ? SaveCore(timer) : SaveCapturedAsync(timer, cancellationToken);
    }

    private ValueTask<DurableTimer> SaveCore(DurableTimer timer)
    {
        lock (_syncRoot)
        {
            var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);

            // Existing wins: a deterministic-id re-invoke upserts idempotently and keeps the first deadline.
            if (_timers.TryGetValue(key, out var existing))
            {
                if (!CanRead(existing)) throw new NotSupportedException("The selected timer adapter cannot read this captured source.");
                return new ValueTask<DurableTimer>(existing.Timer);
            }

            if (timer.HistoryResponsibility is not null && !SupportsHistoryResponsibility)
                throw new NotSupportedException("This timer store lacks scoped owner-history responsibility.");
            var state = new TimerState(timer);
            _timers.Add(key, state);
            if (!Bucket.Runs.TryGetValue(key.WorkflowExecutionId, out var run)) Bucket.Runs[key.WorkflowExecutionId] = run = [];
            run.Add(key);
            if (timer.HistoryResponsibility is { Retired: false }) Bucket.ActiveCaptured[key.WorkflowExecutionId] = checked(Bucket.ActiveCaptured.GetValueOrDefault(key.WorkflowExecutionId) + 1);
            UpdatePending(key, state);
            return new ValueTask<DurableTimer>(timer);
        }
    }

    internal Func<CancellationToken, ValueTask>? BeforeCapturedSourceSave { get; set; }
    internal Func<CancellationToken, ValueTask>? BeforeUnrecordedSourceDischarge { get; set; }

    private async ValueTask<DurableTimer> SaveCapturedAsync(DurableTimer timer, CancellationToken cancellationToken)
    {
        EnsureHistoryScope();
        return await _checkpoints!.ExecuteWriteGateAsync(token => _nativeLiveness is null ? AdmitAsync(token) :
            _nativeLiveness.ExecuteOwnershipAtomicAsync(AdmitAsync, token), cancellationToken);

        async ValueTask<DurableTimer> AdmitAsync(CancellationToken token)
        {
            lock (_syncRoot)
                if (_timers.TryGetValue(new(timer.WorkflowExecutionId, timer.TimerId), out var winner))
                {
                    if (!CanRead(winner)) throw new NotSupportedException("The selected timer adapter cannot read this captured source.");
                    return winner.Timer;
                }
            var history = timer.HistoryResponsibility!;
            foreach (var e in history.PendingEvents)
                if (e.WorkflowExecutionId != timer.WorkflowExecutionId || e.Category != RuntimeHistoryEventCategory.SchedulerLifecycle ||
                    !DurableTimerHistoryFacts.Kinds.Contains(e.Name, StringComparer.Ordinal) ||
                    !e.Metadata.TryGetValue("history.timerId", out var timerId) || timerId != timer.TimerId)
                    throw new InvalidOperationException("Timer responsibility differs from its exact safe source.");
            var native = await _nativeExecutions!.FindAsync(timer.WorkflowExecutionId, token);
            if (native is not null)
            {
                _access!.Current.EnsureTenantScope(native.TenantId);
                if (!history.Policy.IsEquivalentTo(native.HistoryCapturePolicy)) throw new InvalidOperationException("Timer admission differs from its native capture policy.");
                if (native.Status.IsTerminal()) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "A terminal workflow cannot admit a new timer source.");
            }
            else
            {
                lock (_checkpoints.SyncRoot)
                {
                    if (_checkpoints.HistoryHeads.ContainsKey((Scope, timer.WorkflowExecutionId))) throw new InvalidOperationException("Timer history has no matching retained native owner.");
                    if (_checkpoints.HistoryDeletedRuns.Contains((Scope, timer.WorkflowExecutionId)) || _checkpoints.NoRecordTimerCompletionRuns.Contains((Scope, timer.WorkflowExecutionId)))
                        throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The unrecorded workflow already completed its timer responsibility.");
                }
                var ownership = _nativeLiveness is null ? null : await _nativeLiveness.FindAsync(timer.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(timer.WorkflowExecutionId), token);
                if (ownership?.ExecutionLease is not { } lease || lease.IsExpired(_clock.GetUtcNow()) ||
                    ownership.Metadata.GetValueOrDefault(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone) == "true")
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "An unrecorded timer source requires its live execution owner.");
                if (!history.Retired && !history.PendingEvents.Any(e => e.Name == "TimerScheduled"))
                    throw new InvalidOperationException("A new active timer must retain its original scheduling responsibility.");
            }
            if (BeforeCapturedSourceSave is { } before) await before(token);
            token.ThrowIfCancellationRequested();
            return await SaveCore(timer);
        }
    }

    public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is <= 0 or > RuntimeStorePageRequest.MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Due-timer listing limit must be between 1 and {RuntimeStorePageRequest.MaximumLimit}.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var due = _timers.Values
                .Where(state => !IsRetired(state) && CanRead(state))
                .Select(state => state.Timer)
                .Where(timer => timer.DueTime <= asOf)
                .OrderBy(timer => timer.DueTime)
                .ThenBy(timer => timer.TimerId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<DurableTimer>>(due);
        }
    }

    public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<DurableTimer?>(
                _timers.TryGetValue(new TimerKey(workflowExecutionId, timerId), out var timer) && !IsRetired(timer) && CanRead(timer) ? timer.Timer : null);
        }
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, workflowExecutionId, cancellationToken);

    public ValueTask<RuntimeStorePage<DurableTimer>> ListPageAsync(
        DurableTimerPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var timers = _timers.Values
                .Where(state => !IsRetired(state) && CanRead(state))
                .Select(state => state.Timer)
                .Where(timer => StringComparer.Ordinal.Equals(timer.WorkflowExecutionId, query.WorkflowExecutionId))
                .OrderBy(timer => timer.TimerId, StringComparer.Ordinal)
                .ToArray();
            var offset = ParseOffset(query.ContinuationToken);
            var items = timers.Skip(offset).Take(query.Limit).ToArray();
            var nextOffset = checked(offset + items.Length);
            return new ValueTask<RuntimeStorePage<DurableTimer>>(
                new RuntimeStorePage<DurableTimer>(
                    query,
                    items,
                    nextOffset < timers.Length ? nextOffset.ToString() : null));
        }
    }

    public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new TimerKey(workflowExecutionId, timerId);
            if (_timers.TryGetValue(key, out var current) && current.Timer.HistoryResponsibility is not null)
                Retire(key, current, DurableTimerHistoryFacts.CreateRetired(current.Timer, _clock.GetUtcNow(), "owner-removed")!);
            else RemoveLegacyTimer(key);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
        RuntimeDurableTimerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var candidates = _timers.Values
                .Where(state =>
                    !IsRetired(state) && CanRead(state) && state.Timer.DueTime <= request.Now &&
                    (state.VisibleAfter is null || state.VisibleAfter <= request.Now))
                .OrderBy(state => state.VisibleAfter ?? state.Timer.DueTime)
                .ThenBy(state => state.Timer.DueTime)
                .ThenBy(state => state.Timer.TimerId, StringComparer.Ordinal)
                .Take(request.Limit)
                .ToArray();
            var claims = new RuntimeDurableTimerClaim[candidates.Length];
            for (var index = 0; index < candidates.Length; index++)
            {
                var state = candidates[index];
                state.OwnerId = request.OwnerId;
                state.FencingToken = checked(state.FencingToken + 1);
                state.Revision = checked(state.Revision + 1);
                state.ClaimedAt = request.Now;
                state.VisibleAfter = request.Now.Add(request.VisibilityTimeout);
                claims[index] = NewClaim(state);
            }

            return new ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>>(claims);
        }
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Durable timer visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!TryGetCurrentState(claim, out var state))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    _timers.ContainsKey(KeyOf(claim))
                        ? RuntimeDurableTimerClaimTransitionResult.Stale
                        : RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);

            state.Revision = checked(state.Revision + 1);
            state.VisibleAfter = now.Add(visibilityTimeout);
            return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                RuntimeDurableTimerClaimTransitionResult.Applied(NewClaim(state)));
        }
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_timers.ContainsKey(key))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
            if (IsRetired(_timers[key])) return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out var current))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    RuntimeDurableTimerClaimTransitionResult.Stale);

            if (current.Timer.HistoryResponsibility is not null)
                Retire(key, current, DurableTimerHistoryFacts.CreateRetired(current.Timer, _clock.GetUtcNow(), "owner-completed")!);
            else RemoveLegacyTimer(key);
            return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                RuntimeDurableTimerClaimTransitionResult.Applied());
        }
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_timers.ContainsKey(key))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out var state))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    RuntimeDurableTimerClaimTransitionResult.Stale);

            state.OwnerId = null;
            state.ClaimedAt = null;
            state.VisibleAfter = visibleAt;
            state.FailureCount = checked(state.FailureCount + 1);
            state.Revision = checked(state.Revision + 1);
            return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                RuntimeDurableTimerClaimTransitionResult.Applied());
        }
    }

    public ValueTask<bool> HasPendingHistoryAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); cancellationToken.ThrowIfCancellationRequested();
        EnsureHistoryScope();
        lock (_syncRoot) return ValueTask.FromResult(Bucket.PendingRuns.GetValueOrDefault(workflowExecutionId) > 0);
    }

    public async ValueTask<bool> DischargeUnrecordedCompletionAsync(RuntimeCheckpointCommit completion, int limit = 256, CancellationToken cancellationToken = default)
    {
        var workflow = RuntimeHistoryNoRecordCompletion.Validate(completion);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit)); cancellationToken.ThrowIfCancellationRequested(); EnsureHistoryScope();
        _access!.Current.EnsureTenantScope(workflow.TenantId);
        if (_checkpoints is null || _nativeExecutions is null || _nativeLiveness is null || !ReferenceEquals(_nativeExecutions.CheckpointState, _checkpoints))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Unrecorded completion requires its qualified timer owner boundary.");
        var scope = Scope;
        return await _checkpoints.ExecuteWriteGateAsync(token => _nativeLiveness.ExecuteOwnershipAtomicAsync(async ownershipToken =>
        {
            var ownership = await _nativeLiveness.FindAsync(workflow.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(workflow.WorkflowExecutionId), ownershipToken);
            RuntimeExecutionFenceValidator.EnsureCurrent(workflow.WorkflowExecutionId, completion.ExpectedFence!, ownership, _clock.GetUtcNow());
            if (await _nativeExecutions.FindAsync(workflow.WorkflowExecutionId, ownershipToken) is not null)
                throw new InvalidOperationException("A recorded workflow cannot discharge its source history as an unrecorded completion.");
            lock (_checkpoints.SyncRoot)
                if (_checkpoints.HistoryHeads.ContainsKey((scope, workflow.WorkflowExecutionId)) || _checkpoints.HistoryDeletedRuns.Contains((scope, workflow.WorkflowExecutionId)))
                    throw new InvalidOperationException("An acknowledged or deleted history owner cannot discharge an unrecorded completion.");
            if (BeforeUnrecordedSourceDischarge is { } before) await before(ownershipToken);
            lock (_syncRoot)
            {
                if (Bucket.ActiveCaptured.GetValueOrDefault(workflow.WorkflowExecutionId) != 0)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "An active timer still outlives the proposed unrecorded completion.");
                if (!Bucket.PendingByRun.TryGetValue(workflow.WorkflowExecutionId, out var pending)) return true;
                var keys = pending.Values.Take(limit).ToArray();
                foreach (var key in keys)
                {
                    var state = _timers[key];
                    var history = state.Timer.HistoryResponsibility!;
                    if (!history.Retired || !workflow.HistoryCapturePolicy!.IsEquivalentTo(history.Policy))
                        throw new InvalidOperationException("Retired timer responsibility differs from the actual completion's admitted policy.");
                }
                foreach (var key in keys)
                {
                    var state = _timers[key]; var history = state.Timer.HistoryResponsibility!;
                    state.Timer = state.Timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null,
                        HistoryResponsibility = new(history.Policy, [], true) { NoRecordCompletion = history.NoRecordCompletion ?? new(completion.Checkpoint.CheckpointId, workflow.CompletedAt!.Value) } };
                    UpdatePending(key, state);
                }
                if (keys.Length > 0) lock (_checkpoints.SyncRoot) _checkpoints.NoRecordTimerCompletionRuns.Add((scope, workflow.WorkflowExecutionId));
                return Bucket.PendingRuns.GetValueOrDefault(workflow.WorkflowExecutionId) == 0;
            }
        }, token), cancellationToken);
    }

    public ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); cancellationToken.ThrowIfCancellationRequested();
        EnsureHistoryScope();
        var binding = "timer-history:" + RuntimeHistoryIdentity.CreateBinding([Scope, query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var after = InMemoryRuntimeStoreContinuation.Decode(query.ContinuationToken, binding, nameof(query.ContinuationToken));
        lock (_syncRoot)
        {
            var index = Bucket.Pending;
            var low = 0; var high = index.Count;
            while (low < high) { var mid = low + (high - low) / 2; if (after is not null && StringComparer.Ordinal.Compare(index.Keys[mid], after) <= 0) low = mid + 1; else high = mid; }
            var items = new List<DurableTimer>(query.Limit);
            string? last = null;
            while (low < index.Count && items.Count < query.Limit)
            { last = index.Keys[low]; items.Add(_timers[index.Values[low++]].Timer); }
            return ValueTask.FromResult(new RuntimeStorePage<DurableTimer>(query, items,
                low < index.Count ? InMemoryRuntimeStoreContinuation.Encode(binding, last!) : null));
        }
    }

    public async ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureHistoryScope();
        RuntimeHistoryEvent pendingEvent;
        lock (_syncRoot)
        {
            var key = new TimerKey(workflowExecutionId, timerId);
            if (!_timers.TryGetValue(key, out var state)) return true;
            if (state.Timer.HistoryResponsibility is not { } history) return false;
            var item = history.PendingEvents.SingleOrDefault(e => e.HistoryEventId == historyEventId);
            if (item is null) return true;
            pendingEvent = item;
        }
        if (!SupportsHistoryResponsibility) return false;
        var scope = Scope;
        return await _checkpoints!.ExecuteWriteGateAsync(async token =>
        {
            var native = await _nativeExecutions!.FindAsync(workflowExecutionId, token);
            if (native is null) return false;
            _access!.Current.EnsureTenantScope(native.TenantId);
            lock (_checkpoints.SyncRoot)
            {
                if (!_checkpoints.HistoryHeads.ContainsKey((scope, workflowExecutionId)) ||
                    !_checkpoints.HistoryCandidateFingerprints.TryGetValue((scope, workflowExecutionId, historyEventId), out var acknowledged)) return false;
                if (acknowledged != "observation:" + RuntimeHistoryIdentity.Compute(pendingEvent)) throw new InvalidOperationException("Timer history acknowledgment differs from the committed semantic receipt.");
            }
            lock (_syncRoot)
            {
                var key = new TimerKey(workflowExecutionId, timerId);
                if (!_timers.TryGetValue(key, out var state) || state.Timer.HistoryResponsibility is not { } history) return true;
                state.Timer = state.Timer with { HistoryResponsibility = new(history.Policy, history.PendingEvents.Where(e => e.HistoryEventId != historyEventId).ToArray(), history.Retired)
                    { NoRecordCompletion = history.NoRecordCompletion } };
                if (state.Timer.HistoryResponsibility is { Retired: true, PendingEvents.Count: 0 })
                    state.Timer = state.Timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null };
                // History acknowledgments never change the operational claim revision.
                UpdatePending(key, state);
                return true;
            }
        }, cancellationToken);
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(observation); cancellationToken.ThrowIfCancellationRequested(); EnsureHistoryScope();
        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_timers.TryGetValue(key, out var state) || IsRetired(state)) return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out state)) return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Stale);
            Retire(key, state, observation);
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Applied());
        }
    }

    public ValueTask RetireWithHistoryAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation); cancellationToken.ThrowIfCancellationRequested(); EnsureHistoryScope();
        lock (_syncRoot)
        {
            var key = new TimerKey(workflowExecutionId, timerId);
            if (_timers.TryGetValue(key, out var state) && !IsRetired(state)) Retire(key, state, observation);
        }
        return ValueTask.CompletedTask;
    }

    private void EnsureHistoryScope()
    {
        _ = _access?.Current.RequireScope() ?? throw new NotSupportedException("Timer history requires an explicit ordinary scope.");
        if (!SupportsHistoryResponsibility) throw new NotSupportedException("The selected timer adapter has no qualified native history boundary.");
    }

    private void Retire(TimerKey key, TimerState state, RuntimeHistoryObservation? observation)
    {
        EnsureHistoryScope();
        if (IsRetired(state)) return;
        var history = state.Timer.HistoryResponsibility ?? throw new InvalidOperationException("A legacy timer cannot acquire history retroactively.");
        if (observation is not null && (!history.Policy.IsEquivalentTo(observation.Policy) || observation.Event.WorkflowExecutionId != key.WorkflowExecutionId ||
            !observation.Event.Metadata.TryGetValue("history.timerId", out var timerId) || timerId != key.TimerId))
            throw new InvalidOperationException("Timer history responsibility differs from its exact source.");
        var next = new DurableTimerHistoryResponsibility(history.Policy, observation is null ? history.PendingEvents : history.PendingEvents.Append(observation.Event).ToArray(), true);
        state.Timer = state.Timer with { HistoryResponsibility = next };
        Bucket.ActiveCaptured[key.WorkflowExecutionId] = checked(Bucket.ActiveCaptured.GetValueOrDefault(key.WorkflowExecutionId) - 1);
        state.OwnerId = null; state.ClaimedAt = null; state.Revision = checked(state.Revision + 1);
        UpdatePending(key, state);
    }

    private bool CanRead(TimerState state) => state.Timer.HistoryResponsibility is null || SupportsHistoryResponsibility;
    private void RemoveLegacyTimer(TimerKey key)
    {
        _timers.Remove(key);
        if (Bucket.Runs.TryGetValue(key.WorkflowExecutionId, out var run))
        {
            run.Remove(key);
            if (run.Count == 0) Bucket.Runs.Remove(key.WorkflowExecutionId);
        }
    }
    private static bool IsRetired(TimerState state) => state.Timer.HistoryResponsibility?.Retired == true;
    private static string HistoryKey(TimerKey key) => RuntimeHistoryIdentity.CreateBinding([key.WorkflowExecutionId, key.TimerId]);
    private void UpdatePending(TimerKey key, TimerState state)
    {
        var index = Bucket.Pending; var order = HistoryKey(key);
        var wasPending = index.ContainsKey(order);
        if (state.Timer.HistoryResponsibility?.PendingEvents.Count > 0)
        {
            if (index.TryGetValue(order, out var existing) && existing != key) throw new InvalidOperationException("Timer history index identity collision.");
            index[order] = key;
            if (!Bucket.PendingByRun.TryGetValue(key.WorkflowExecutionId, out var runPending)) Bucket.PendingByRun[key.WorkflowExecutionId] = runPending = new(StringComparer.Ordinal);
            runPending[order] = key;
            if (!wasPending) Bucket.PendingRuns[key.WorkflowExecutionId] = checked(Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) + 1);
        }
        else if (index.Remove(order))
        {
            Bucket.PendingByRun.GetValueOrDefault(key.WorkflowExecutionId)?.Remove(order);
            Bucket.PendingRuns[key.WorkflowExecutionId] = Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) - 1;
        }
    }

    private bool TryGetCurrentState(RuntimeDurableTimerClaim claim, out TimerState state)
    {
        if (_timers.TryGetValue(KeyOf(claim), out state!) && !IsRetired(state) &&
            state.Revision == claim.Revision &&
            state.FencingToken == claim.FencingToken &&
            StringComparer.Ordinal.Equals(state.OwnerId, claim.OwnerId))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static TimerKey KeyOf(RuntimeDurableTimerClaim claim) =>
        new(claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);

    private static int ParseOffset(string? continuationToken)
    {
        if (continuationToken is null)
            return 0;
        if (!int.TryParse(continuationToken, out var offset) || offset < 0)
            throw new ArgumentException("The durable-timer page continuation is invalid.", nameof(continuationToken));
        return offset;
    }

    private static RuntimeDurableTimerClaim NewClaim(TimerState state) =>
        new(
            state.Timer,
            state.OwnerId!,
            state.FencingToken,
            state.Revision,
            state.ClaimedAt!.Value,
            state.VisibleAfter!.Value,
            state.FailureCount);

    internal readonly record struct TimerKey(string WorkflowExecutionId, string TimerId);

    internal sealed class TimerState(DurableTimer timer)
    {
        public DurableTimer Timer { get; set; } = timer;
        public string? OwnerId { get; set; }
        public long FencingToken { get; set; }
        public long Revision { get; set; } = 1;
        public DateTimeOffset? ClaimedAt { get; set; }
        public DateTimeOffset? VisibleAfter { get; set; }
        public int FailureCount { get; set; }
    }
}

/// <summary>Shared scoped partitions for the default timer owner and its pending source history.</summary>
public sealed class InMemoryDurableTimerStoreState
{
    internal object SyncRoot { get; } = new();
    internal Dictionary<string, Bucket> Scopes { get; } = new(StringComparer.Ordinal);
    internal sealed class Bucket
    {
        internal Dictionary<InMemoryDurableTimerStore.TimerKey, InMemoryDurableTimerStore.TimerState> Timers { get; } = new();
        internal SortedList<string, InMemoryDurableTimerStore.TimerKey> Pending { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> PendingRuns { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, HashSet<InMemoryDurableTimerStore.TimerKey>> Runs { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> ActiveCaptured { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, SortedList<string, InMemoryDurableTimerStore.TimerKey>> PendingByRun { get; } = new(StringComparer.Ordinal);
    }
}
