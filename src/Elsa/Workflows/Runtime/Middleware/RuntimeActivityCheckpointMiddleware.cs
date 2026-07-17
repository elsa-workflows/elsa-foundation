using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// The activity pipeline's <c>Checkpoint</c> slot: persists the checkpoint commits a handler assembled and staged on
/// the dispatch workspace in the earlier <c>Invoke</c> slot (ADR 0029, Move 2). Mirrors
/// <see cref="RuntimeWorkflowCheckpointMiddleware"/>. Handlers that stage nothing leave the list empty and this slot is
/// a no-op for them.
/// </summary>
/// <remarks>
/// This slot is also where a bookmark-created checkpoint's lifecycle notification fires on the pipeline dispatch path
/// (spec 089 D): the create handler stages its commit here rather than committing inline, so — unlike its direct
/// <c>HandleAsync</c> path, which notifies right after its own inline commit — the post-commit
/// <see cref="BookmarkLifecycleNotifier.NotifyCreatedAsync"/> must run here, after the staged commit lands. Consume
/// notifications are unaffected: <c>BookmarkConsumptionCheckpointService</c> commits and notifies inline and never
/// stages through this slot. Observer failures are caught and logged inside the notifier and never fault the run.
/// </remarks>
public sealed class RuntimeActivityCheckpointMiddleware(
    RuntimeCheckpointCommitter checkpointCommitter,
    BookmarkLifecycleNotifier? bookmarkLifecycleNotifier = null) : IActivityRuntimeMiddleware
{
    public async ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Drain in stage order, one committer call per staged entry — never fold or batch (that is the coalescing
        // layer's job; batching here would change W9 boundary detection and W5 fencing granularity).
        foreach (var commit in context.Workspace.PendingCheckpointCommits)
        {
            await checkpointCommitter.CommitAsync(commit, context.Workspace.CancellationToken);
            await NotifyBookmarksCreatedAsync(commit);
        }

        await next(context);
    }

    /// <summary>
    /// Fires the bookmark-created lifecycle notification for a just-committed bookmark-created checkpoint (spec 089 D).
    /// This is the pipeline-path counterpart to <c>WorkflowCreateBookmarkSchedulerWorkHandler</c>'s direct-path
    /// post-commit notify: a bookmark created through the ADR 0029 pipeline stages its commit for this slot, so the
    /// notification must fire here (the drainer dispatches CreateBookmark through the pipeline, not the direct path).
    /// Gated on the checkpoint name so non-bookmark checkpoints cost only a string compare.
    /// </summary>
    private async ValueTask NotifyBookmarksCreatedAsync(RuntimeCheckpointCommit commit)
    {
        if (bookmarkLifecycleNotifier is null || !StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.BookmarkCreated))
            return;

        foreach (var change in commit.StateChanges.Bookmarks)
        {
            if (change is { Operation: RuntimeStateChangeOperation.Upsert, State: { } bookmark })
                await bookmarkLifecycleNotifier.NotifyCreatedAsync(bookmark, CancellationToken.None);
        }
    }
}
