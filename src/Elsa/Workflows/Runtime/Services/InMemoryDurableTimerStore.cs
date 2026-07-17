using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Process-local <see cref="IDurableTimerStore"/>. Timers are held in memory only, so a <c>Delay</c> backed
/// by this store is <b>not</b> restart-durable — a process restart forgets every pending timer. Compose a
/// durable persistence provider (e.g. the Groundwork bridge) to make timers survive restarts.
/// </summary>
public sealed class InMemoryDurableTimerStore : IDurableTimerStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<TimerKey, TimerState> _timers = new();

    public bool SupportsClaimTransitions => true;

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

            _timers.Add(key, new TimerState(timer));
            return new ValueTask<DurableTimer>(timer);
        }
    }

    public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-timer listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var due = _timers.Values
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
                _timers.TryGetValue(new TimerKey(workflowExecutionId, timerId), out var timer) ? timer.Timer : null);
        }
    }

    public ValueTask<IReadOnlyCollection<DurableTimer>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<DurableTimer>>(_timers.Values
                .Select(state => state.Timer)
                .Where(timer => StringComparer.Ordinal.Equals(timer.WorkflowExecutionId, workflowExecutionId))
                .OrderBy(timer => timer.TimerId, StringComparer.Ordinal)
                .ToArray());
        }
    }

    public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _timers.Remove(new TimerKey(workflowExecutionId, timerId));
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
                    state.Timer.DueTime <= request.Now &&
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
            if (!TryGetCurrentState(claim, out _))
                return new ValueTask<RuntimeDurableTimerClaimTransitionResult>(
                    RuntimeDurableTimerClaimTransitionResult.Stale);

            _timers.Remove(key);
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

    private bool TryGetCurrentState(RuntimeDurableTimerClaim claim, out TimerState state)
    {
        if (_timers.TryGetValue(KeyOf(claim), out state!) &&
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

    private static RuntimeDurableTimerClaim NewClaim(TimerState state) =>
        new(
            state.Timer,
            state.OwnerId!,
            state.FencingToken,
            state.Revision,
            state.ClaimedAt!.Value,
            state.VisibleAfter!.Value,
            state.FailureCount);

    private readonly record struct TimerKey(string WorkflowExecutionId, string TimerId);

    private sealed class TimerState(DurableTimer timer)
    {
        public DurableTimer Timer { get; } = timer;
        public string? OwnerId { get; set; }
        public long FencingToken { get; set; }
        public long Revision { get; set; } = 1;
        public DateTimeOffset? ClaimedAt { get; set; }
        public DateTimeOffset? VisibleAfter { get; set; }
        public int FailureCount { get; set; }
    }
}
