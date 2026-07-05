using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Middleware;

/// <summary>
/// The activity pipeline's <c>Invoke</c> slot: runs the work item's selected handler (staged by the dispatcher) in the
/// before-<c>next</c> direction, so a handler's phase results are available to the later slots (`Checkpoint`,
/// `PostCommit`) that apply them (ADR 0029, Move 2). Mirrors <see cref="RuntimeWorkflowInvokeMiddleware"/>. When no
/// handler is staged this slot is a no-op.
/// </summary>
public sealed class RuntimeActivityInvokeMiddleware(IWorkflowEngineTracer? tracer = null) : IActivityRuntimeMiddleware
{
    private readonly IWorkflowEngineTracer _tracer = tracer ?? NullWorkflowEngineTracer.Instance;

    public async ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Workspace.InvokeHandler is { } invokeHandler)
        {
            // Clear before running so the pipeline terminal can detect a missing Invoke slot (an unconsumed handler).
            context.Workspace.InvokeHandler = null;

            // MS-9: the activity-execution span wraps only the handler invocation and nests under the dispatch span via
            // Activity.Current. The InvokeHandler clear above stays before the span starts so terminal-detection is
            // unchanged; null when tracing is inactive.
            using var activity = _tracer.StartActivityExecution(context.WorkItem);
            await invokeHandler(context);
        }

        await next(context);
    }
}
