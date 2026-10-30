using Elsa.Workflows.Runtime.Core.Contracts;
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
public sealed class DurableTimerScheduler : IDurableTimerScheduler
{
    private readonly IDurableTimerStore _store;
    private readonly IDeferredEventPublisher? _events;
    private readonly ILogger<DurableTimerScheduler> _logger = NullLogger<DurableTimerScheduler>.Instance;

    public DurableTimerScheduler(IDurableTimerStore store) => _store = store;

    public DurableTimerScheduler(IDurableTimerStore store, IDeferredEventPublisher events,
        ILogger<DurableTimerScheduler>? logger = null) : this(store)
    {
        _events = events;
        _logger = logger ?? NullLogger<DurableTimerScheduler>.Instance;
    }

    public async ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
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
