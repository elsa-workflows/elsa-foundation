using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

/// <summary>Default provider-neutral resource inventory and cleanup for one activity execution scope.</summary>
public sealed class ActivityScopeCleanupStore(
    IBookmarkStateStore bookmarkStateStore,
    IDurableTimerStore durableTimerStore,
    IWorkflowSchedulerWorkQueue schedulerWorkQueue) : IActivityScopeCleanupStore
{
    private readonly IWorkflowExecutionStateStore? _historyExecutions;
    public ActivityScopeCleanupStore(IBookmarkStateStore bookmarks, IDurableTimerStore timers, IWorkflowSchedulerWorkQueue queue,
        IWorkflowExecutionStateStore historyExecutions) : this(bookmarks, timers, queue)
    { _historyExecutions = historyExecutions ?? throw new ArgumentNullException(nameof(historyExecutions)); }

    public bool SupportsHistoryBookmarks => _historyExecutions is not null;

    public async ValueTask<ActivityScopeCleanupRequest> CaptureAsync(
        string workflowExecutionId,
        string executionScopeId,
        IReadOnlySet<string> activityExecutionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);
        ArgumentNullException.ThrowIfNull(activityExecutionIds);
        cancellationToken.ThrowIfCancellationRequested();

        var bookmarks = (await bookmarkStateStore.ListAllBookmarkStatesAsync(workflowExecutionId, cancellationToken))
            .Where(bookmark => activityExecutionIds.Contains(bookmark.ActivityExecutionId))
            .OrderBy(bookmark => bookmark.BookmarkId, StringComparer.Ordinal)
            .ToArray();
        var stimuli = bookmarks.Select(bookmark => (bookmark.StimulusType, bookmark.StimulusHash)).ToHashSet();
        var timers = (await RuntimeOperationalStorePagingExtensions.ListAllAsync(durableTimerStore, workflowExecutionId, cancellationToken))
            .Where(timer =>
                (timer.ActivityExecutionId is { } activityExecutionId && activityExecutionIds.Contains(activityExecutionId)) ||
                (timer.ExecutionScopeId is { } timerScopeId && activityExecutionIds.Contains(timerScopeId)) ||
                stimuli.Contains((timer.StimulusType, timer.StimulusHash)))
            .OrderBy(timer => timer.TimerId, StringComparer.Ordinal)
            .ToArray();
        var workItems = (await schedulerWorkQueue.ListAllAsync(workflowExecutionId, cancellationToken))
            .Where(item => item.ExecutionScopeId is { } scopeId && activityExecutionIds.Contains(scopeId))
            .OrderBy(item => item.RecordedAt)
            .ThenBy(item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray();

        RuntimeHistoryCapturePolicy? policy = null;
        if (_historyExecutions is not null)
        {
            try { policy = (await _historyExecutions.FindAsync(workflowExecutionId, cancellationToken))?.HistoryCapturePolicy; }
            catch (Exception exception) when (RuntimeHistoryReadFailures.IsMalformed(exception))
            { throw new RuntimeHistoryIntegrityException(workflowExecutionId, "the cleanup owner could not interpret its admitted policy", exception); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && RuntimeHistoryReadFailures.IsUnavailable(exception))
            { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The history cleanup source is unavailable.", exception); }
        }
        return new ActivityScopeCleanupRequest(
            workflowExecutionId,
            executionScopeId,
            activityExecutionIds.Order(StringComparer.Ordinal).ToArray(),
            bookmarks.Select(item => item.BookmarkId).ToArray(),
            timers.Select(item => item.TimerId).ToArray(),
            workItems.Select(item => item.WorkItemId).ToArray())
        { HistoryBookmarks = RuntimeHistorySourceFacts.IsFinalProfile(policy) ? Array.AsReadOnly(bookmarks.Select(ActivityExecutionBookmarkSummary.From).ToArray()) : null };
    }

    public async ValueTask ApplyAsync(ActivityScopeCleanupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var bookmarkId in request.BookmarkIds)
            await bookmarkStateStore.DeleteAsync(request.WorkflowExecutionId, bookmarkId, cancellationToken);
        foreach (var timerId in request.TimerIds)
            await durableTimerStore.DeleteAsync(request.WorkflowExecutionId, timerId, cancellationToken);
        foreach (var workItemId in request.SchedulerWorkItemIds)
            await schedulerWorkQueue.DeleteAsync(request.WorkflowExecutionId, workItemId, cancellationToken);
    }
}
