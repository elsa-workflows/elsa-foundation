using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// The workflow pipeline's <c>Invoke</c> slot: runs the work item's selected handler (staged by the dispatcher) in the
/// before-<c>next</c> direction, so a handler's phase results are available to the later slots (`Checkpoint`,
/// `PostCommit`) that apply them (ADR 0029, Move 2). When no handler is staged (e.g. the handler runs as the pipeline
/// terminal instead), this slot is a no-op.
/// </summary>
public sealed class RuntimeWorkflowInvokeMiddleware : IWorkflowRuntimeMiddleware
{
    public async ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Workspace.InvokeHandler is { } invokeHandler)
        {
            // Clear before running so the pipeline terminal can detect a missing Invoke slot (an unconsumed handler).
            context.Workspace.InvokeHandler = null;
            await invokeHandler(context);
        }

        await next(context);
    }
}
