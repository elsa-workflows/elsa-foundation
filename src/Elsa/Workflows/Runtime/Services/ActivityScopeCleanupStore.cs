using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Default provider-neutral resource inventory and cleanup for one activity execution scope.</summary>
public sealed class ActivityScopeCleanupStore(
    IBookmarkStateStore bookmarkStateStore,
    IDurableTimerStore durableTimerStore,
    IWorkflowSchedulerWorkQueue schedulerWorkQueue) : IActivityScopeCleanupStore
{
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

        return new ActivityScopeCleanupRequest(
            workflowExecutionId,
            executionScopeId,
            activityExecutionIds.Order(StringComparer.Ordinal).ToArray(),
            bookmarks.Select(item => item.BookmarkId).ToArray(),
            timers.Select(item => item.TimerId).ToArray(),
            workItems.Select(item => item.WorkItemId).ToArray());
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
