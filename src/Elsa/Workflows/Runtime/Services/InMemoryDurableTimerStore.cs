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
    private readonly Dictionary<TimerKey, DurableTimer> _timers = new();

    public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);

            // Existing wins: a deterministic-id re-invoke upserts idempotently and keeps the first deadline.
            if (_timers.TryGetValue(key, out var existing))
                return new ValueTask<DurableTimer>(existing);

            _timers.Add(key, timer);
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
                _timers.TryGetValue(new TimerKey(workflowExecutionId, timerId), out var timer) ? timer : null);
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

    private readonly record struct TimerKey(string WorkflowExecutionId, string TimerId);
}
