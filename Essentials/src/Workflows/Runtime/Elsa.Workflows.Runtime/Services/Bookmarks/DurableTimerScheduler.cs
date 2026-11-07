using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

/// <summary>
/// Default <see cref="IDurableTimerScheduler"/>. A thin, testable seam that forwards to the configured
/// <see cref="IDurableTimerStore"/>, isolating suspending activities from the store contract.
/// </summary>
public sealed class DurableTimerScheduler : IDurableTimerScheduler, IRuntimeHistoryTimerSource
{
    private readonly IDurableTimerStore _store;
    private readonly IDeferredEventPublisher? _events;
    private readonly IWorkflowExecutionStateStore? _historyExecutions;
    private readonly ILogger<DurableTimerScheduler> _logger = NullLogger<DurableTimerScheduler>.Instance;

    public DurableTimerScheduler(IDurableTimerStore store) => _store = store;

    public DurableTimerScheduler(IDurableTimerStore store, IDeferredEventPublisher events,
        ILogger<DurableTimerScheduler>? logger = null) : this(store)
    {
        _events = events;
        _logger = logger ?? NullLogger<DurableTimerScheduler>.Instance;
    }

    public DurableTimerScheduler(IDurableTimerStore store, IDeferredEventPublisher events,
        IWorkflowExecutionStateStore historyExecutions, ILogger<DurableTimerScheduler>? logger = null) : this(store, events, logger)
    { _historyExecutions = historyExecutions; }

    bool IRuntimeHistoryTimerSource.CanRecordHistory(IDurableTimerStore sourceStore) =>
        ReferenceEquals(sourceStore, _store) && _historyExecutions is not null && sourceStore is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true };

    public async ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (timer.HistoryResponsibility is null && _store is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } && _historyExecutions is not null)
        {
            WorkflowExecutionState? owner;
            try { owner = await _historyExecutions.FindAsync(timer.WorkflowExecutionId, cancellationToken); }
            catch (Exception exception) when (exception is not (OperationCanceledException or ArgumentException or InvalidDataException or RuntimeSchedulerWorkUnavailableException))
            { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Timer source history dependency is unavailable.", exception); }
            if (owner?.HistoryCapturePolicy is { } policy && DurableTimerHistoryFacts.CreateScheduled(timer, policy) is { } scheduled)
                timer = timer with { HistoryResponsibility = new(policy, [scheduled.Event], false) };
        }
        var stored = await _store.SaveAsync(timer, cancellationToken);
        if (_events is not null)
        {
            try { await _events.Publish(new DurableTimerScheduled(stored.DueTime), cancellationToken); }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                _logger.LogWarning(exception, "Durable timer scheduled notification failed; periodic recovery remains available");
            }
        }
        return stored;
    }
}
