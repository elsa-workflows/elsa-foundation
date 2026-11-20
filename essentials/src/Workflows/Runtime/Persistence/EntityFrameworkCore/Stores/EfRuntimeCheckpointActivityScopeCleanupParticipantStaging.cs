using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Fences exact scope-cancellation resource deletions in the checkpoint's shared EF transaction.</summary>
internal static class EfRuntimeCheckpointActivityScopeCleanupParticipantStaging
{
    public static async ValueTask StageAsync(
        RuntimeDbContext context,
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups,
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cleanups);
        if (cleanups.Count == 0)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        foreach (var cleanup in cleanups)
            ArgumentNullException.ThrowIfNull(cleanup);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Activity-scope cleanup requires the caller-owned checkpoint transaction.");

        var staged = cleanups.Select(cleanup => new StagedCleanup(
                Identify(cleanup.BookmarkIds, bookmarkId => EfBookmarkStateStore.CreateId(scope, cleanup.WorkflowExecutionId, bookmarkId)),
                Identify(cleanup.TimerIds, timerId => EfRuntimeOperationalStoreSupport.CompositeId(scope, cleanup.WorkflowExecutionId, timerId)),
                Identify(cleanup.SchedulerWorkItemIds, workItemId => EfRuntimeOperationalStoreSupport.CompositeId(scope, cleanup.WorkflowExecutionId, workItemId)),
                cleanup.WorkflowExecutionId))
            .ToArray();

        // One read per resource type for the whole cleanup set. Each id is the immutable physical identity of the row
        // it deletes, so a resource the batch does not return is absent exactly as it was for the per-item reads, and
        // a returned row still goes through its store's checked read below before it is removed.
        var bookmarks = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.SelectMany(entry => entry.Bookmarks.Select(resource => resource.RowId)).ToArray(),
            batch => context.Bookmarks.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);
        var timers = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.SelectMany(entry => entry.Timers.Select(resource => resource.RowId)).ToArray(),
            batch => context.DurableTimers.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);
        var workItems = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.SelectMany(entry => entry.WorkItems.Select(resource => resource.RowId)).ToArray(),
            batch => context.SchedulerWorkItems.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);

        foreach (var entry in staged)
        {
            Remove(entry.Bookmarks, bookmarks, context.Bookmarks, cancellationToken,
                (row, bookmarkId, rowId) => EfBookmarkStateStore.MapChecked(row, scope, entry.WorkflowExecutionId, bookmarkId, rowId));
            Remove(entry.Timers, timers, context.DurableTimers, cancellationToken,
                (row, timerId, _) => EfDurableTimerStore.ReadChecked(row, scope, entry.WorkflowExecutionId, timerId));
            Remove(entry.WorkItems, workItems, context.SchedulerWorkItems, cancellationToken,
                (row, workItemId, _) => EfSchedulerWorkQueueStore.ReadChecked(row, scope, entry.WorkflowExecutionId, workItemId));
        }
    }

    private static (string ResourceId, string RowId)[] Identify(
        IEnumerable<string> resourceIds,
        Func<string, string> rowId) =>
        resourceIds.Select(resourceId => (resourceId, rowId(resourceId))).ToArray();

    /// <summary>
    /// Deletes the resources of one kind that exist, leaving an absent one alone. The checked read is what refuses a
    /// corrupt row instead of silently removing or silently missing it, so it runs before every removal.
    /// </summary>
    private static void Remove<TEntity>(
        (string ResourceId, string RowId)[] resources,
        Dictionary<string, TEntity> rows,
        DbSet<TEntity> set,
        CancellationToken cancellationToken,
        Action<TEntity, string, string> readChecked)
        where TEntity : class
    {
        foreach (var (resourceId, rowId) in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.GetValueOrDefault(rowId) is not { } row)
                continue;
            readChecked(row, resourceId, rowId);
            set.Remove(row);
        }
    }

    private sealed record StagedCleanup(
        (string ResourceId, string RowId)[] Bookmarks,
        (string ResourceId, string RowId)[] Timers,
        (string ResourceId, string RowId)[] WorkItems,
        string WorkflowExecutionId);
}
