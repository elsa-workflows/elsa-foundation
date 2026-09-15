using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Fences exact scope-cancellation resource deletions in the checkpoint's shared EF transaction.</summary>
internal static class EfRuntimeCheckpointActivityScopeCleanupParticipantStaging
{
    public static async ValueTask StageAsync(
        BookmarkStateDbContext context,
        ActivityScopeCleanupRequest cleanup,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Activity-scope cleanup requires the caller-owned checkpoint transaction.");

        foreach (var bookmarkId in cleanup.BookmarkIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = EfBookmarkStateStore.CreateId(scope, cleanup.WorkflowExecutionId, bookmarkId);
            var row = await context.Bookmarks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                continue;
            _ = EfBookmarkStateStore.MapChecked(row, scope, cleanup.WorkflowExecutionId, bookmarkId, id);
            context.Bookmarks.Remove(row);
        }

        foreach (var timerId in cleanup.TimerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, cleanup.WorkflowExecutionId, timerId);
            var row = await context.DurableTimers.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                continue;
            _ = EfDurableTimerStore.ReadChecked(row, scope, cleanup.WorkflowExecutionId, timerId);
            context.DurableTimers.Remove(row);
        }

        foreach (var workItemId in cleanup.SchedulerWorkItemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, cleanup.WorkflowExecutionId, workItemId);
            var row = await context.SchedulerWorkItems.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                continue;
            _ = EfSchedulerWorkQueueStore.ReadChecked(row, scope, cleanup.WorkflowExecutionId, workItemId);
            context.SchedulerWorkItems.Remove(row);
        }
    }
}
