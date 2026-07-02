using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// The workflow pipeline's <c>Checkpoint</c> slot: persists the checkpoint commit a terminal handler staged on the
/// dispatch workspace (ADR 0029, Move 2). It runs after the inner pipeline (including the handler) has assembled the
/// commit, so the actual <see cref="RuntimeCheckpointCommitter.CommitAsync"/> now happens in this named slot instead of
/// inline in the handler. Handlers that stage nothing leave the commit null and this slot is a no-op for them.
/// </summary>
/// <remarks>
/// Transitional shape: because a handler is still the pipeline's terminal, this middleware commits during its
/// <em>after-<c>next</c></em> unwind (once the handler has run and staged). When later slices move a handler's commit
/// <em>assembly</em> out of the terminal into the <c>Invoke</c> slot, this can commit in the forward (before-<c>next</c>)
/// direction so the <c>PostCommit</c> slot naturally follows it.
/// </remarks>
public sealed class RuntimeWorkflowCheckpointMiddleware(RuntimeCheckpointCommitter checkpointCommitter) : IWorkflowRuntimeMiddleware
{
    public async ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        await next(context);

        if (context.Workspace.PendingCheckpointCommit is { } commit)
            await checkpointCommitter.CommitAsync(commit);
    }
}
