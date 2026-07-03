using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// The workflow pipeline's <c>Checkpoint</c> slot: persists the checkpoint commit the handler assembled and staged on
/// the dispatch workspace in the earlier <c>Invoke</c> slot (ADR 0029, Move 2). The commit runs in the before-<c>next</c>
/// direction so the <c>PostCommit</c> slot naturally follows it. Handlers that stage nothing leave the commit null and
/// this slot is a no-op for them.
/// </summary>
public sealed class RuntimeWorkflowCheckpointMiddleware(RuntimeCheckpointCommitter checkpointCommitter) : IWorkflowRuntimeMiddleware
{
    public async ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)
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
