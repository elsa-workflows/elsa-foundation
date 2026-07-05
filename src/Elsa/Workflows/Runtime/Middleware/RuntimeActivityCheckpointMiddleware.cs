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
public sealed class RuntimeActivityCheckpointMiddleware(RuntimeCheckpointCommitter checkpointCommitter) : IActivityRuntimeMiddleware
{
    public async ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Drain in stage order, one committer call per staged entry — never fold or batch (that is the coalescing
        // layer's job; batching here would change W9 boundary detection and W5 fencing granularity).
        foreach (var commit in context.Workspace.PendingCheckpointCommits)
            await checkpointCommitter.CommitAsync(commit, context.Workspace.CancellationToken);

        await next(context);
    }
}
