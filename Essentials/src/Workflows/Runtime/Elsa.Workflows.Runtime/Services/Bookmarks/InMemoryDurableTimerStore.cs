using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

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
    public bool SupportsHistoryResponsibility
    {
        get { try { return _access?.Current.RequireScope() is not null; } catch (InvalidOperationException) { return false; } }
    }


    internal void DeleteForWorkflowExecution(string workflowExecutionId)
    {
        lock (_syncRoot)
        {
            foreach (var key in _timers.Keys
                         .Where(key => StringComparer.Ordinal.Equals(key.WorkflowExecutionId, workflowExecutionId))
                         .ToArray())
            {
                _timers.Remove(key);
                if (Bucket.Pending.Remove(HistoryKey(key))) Bucket.PendingRuns[key.WorkflowExecutionId] = Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) - 1;
            }
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

        lock (_syncRoot)
        {
            var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);

            // Existing wins: a deterministic-id re-invoke upserts idempotently and keeps the first deadline.
            if (_timers.TryGetValue(key, out var existing))
                return new ValueTask<DurableTimer>(existing.Timer);

            if (timer.HistoryResponsibility is not null && !SupportsHistoryResponsibility)
                throw new NotSupportedException("This timer store lacks scoped owner-history responsibility.");
            var state = new TimerState(timer);
            _timers.Add(key, state);
            UpdatePending(key, state);
            return new ValueTask<DurableTimer>(timer);
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
            else _timers.Remove(key);
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
            else _timers.Remove(key);
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
        _ = _access?.Current.RequireScope() ?? throw new NotSupportedException("Pending timer history requires an explicit ordinary scope.");
        lock (_syncRoot) return ValueTask.FromResult(Bucket.PendingRuns.GetValueOrDefault(workflowExecutionId) > 0);
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

    public ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureHistoryScope();
        lock (_syncRoot)
        {
            var key = new TimerKey(workflowExecutionId, timerId);
            if (!_timers.TryGetValue(key, out var state) || state.Timer.HistoryResponsibility is not { } history ||
                !history.PendingEvents.Any(e => e.HistoryEventId == historyEventId)) return ValueTask.FromResult(false);
            state.Timer = state.Timer with { HistoryResponsibility = new(history.Policy, history.PendingEvents.Where(e => e.HistoryEventId != historyEventId).ToArray(), history.Retired) };
            if (state.Timer.HistoryResponsibility is { Retired: true, PendingEvents.Count: 0 })
                state.Timer = state.Timer with { Input = null, Metadata = null, PayloadType = null, ProviderId = null };
            // History acknowledgements never change the operational claim revision.
            UpdatePending(key, state);
            return ValueTask.FromResult(true);
        }
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

    private void EnsureHistoryScope() => _ = _access?.Current.RequireScope() ?? throw new NotSupportedException("Timer history requires an explicit ordinary scope.");

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
        state.OwnerId = null; state.ClaimedAt = null; state.Revision = checked(state.Revision + 1);
        UpdatePending(key, state);
    }

    private bool CanRead(TimerState state) => state.Timer.HistoryResponsibility is null || SupportsHistoryResponsibility;
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
            if (!wasPending) Bucket.PendingRuns[key.WorkflowExecutionId] = checked(Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) + 1);
        }
        else if (index.Remove(order)) Bucket.PendingRuns[key.WorkflowExecutionId] = Bucket.PendingRuns.GetValueOrDefault(key.WorkflowExecutionId) - 1;
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
    }
}
