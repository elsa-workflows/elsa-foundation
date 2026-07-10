using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Fans a committed bookmark lifecycle transition out to every registered <see cref="IBookmarkLifecycleObserver"/>
/// (spec 089 D). It exists so the two commit sites — the bookmark-created checkpoint
/// (<see cref="WorkflowCreateBookmarkSchedulerWorkHandler"/>) and the bookmark-consumed checkpoint
/// (<see cref="BookmarkConsumptionCheckpointService"/>) — share one notification path with one failure policy,
/// rather than each re-implementing the catch-and-log loop.
/// </summary>
/// <remarks>
/// Failure policy: this fires on the RUN path, so an observer that throws is caught and logged and NEVER faults
/// the run (contrast <see cref="IWorkflowTriggerIndexObserver"/>, whose exceptions fail the publish). Every
/// observer is invoked even if an earlier one threw. Absent observers is the common, cheap case.
/// </remarks>
public sealed class BookmarkLifecycleNotifier
{
    private readonly IReadOnlyList<IBookmarkLifecycleObserver> _observers;
    private readonly ILogger<BookmarkLifecycleNotifier> _logger;

    public BookmarkLifecycleNotifier(
        IEnumerable<IBookmarkLifecycleObserver>? observers = null,
        ILogger<BookmarkLifecycleNotifier>? logger = null)
    {
        _observers = observers?.ToArray() ?? [];
        _logger = logger ?? NullLogger<BookmarkLifecycleNotifier>.Instance;
    }

    /// <summary>Notifies observers that a bookmark was durably created; observer throws are caught and logged.</summary>
    public ValueTask NotifyCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        NotifyAsync(bookmark, created: true, cancellationToken);

    /// <summary>Notifies observers that a bookmark was durably consumed; observer throws are caught and logged.</summary>
    public ValueTask NotifyConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        NotifyAsync(bookmark, created: false, cancellationToken);

    private async ValueTask NotifyAsync(BookmarkState bookmark, bool created, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        if (_observers.Count == 0)
            return;

        foreach (var observer in _observers)
        {
            try
            {
                if (created)
                    await observer.OnBookmarkCreatedAsync(bookmark, cancellationToken);
                else
                    await observer.OnBookmarkConsumedAsync(bookmark, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Run path: a stale projection is tolerated, an aborted durable run is not. Log and continue so
                // one bad observer neither faults the workflow nor starves the others.
                _logger.LogError(
                    exception,
                    "Bookmark lifecycle observer '{Observer}' threw while handling the {Transition} of bookmark '{BookmarkId}' (workflow execution '{WorkflowExecutionId}'); ignoring so the run is not faulted.",
                    observer.GetType().FullName,
                    created ? "creation" : "consumption",
                    bookmark.BookmarkId,
                    bookmark.WorkflowExecutionId);
            }
        }
    }
}
