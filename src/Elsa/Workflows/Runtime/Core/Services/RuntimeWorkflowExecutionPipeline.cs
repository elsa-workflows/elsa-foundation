using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Runs the workflow runtime middleware (in <see cref="RuntimePipelinePlan"/> order) around a terminal delegate.
/// </summary>
public sealed class RuntimeWorkflowExecutionPipeline : IRuntimeWorkflowExecutionPipeline
{
    private readonly IReadOnlyList<IWorkflowRuntimeMiddleware> _middleware;

    public RuntimeWorkflowExecutionPipeline(RuntimePipelinePlan plan, IServiceProvider serviceProvider)
    {
        _middleware = RuntimeExecutionPipelineCore.ResolveMiddleware<IWorkflowRuntimeMiddleware>(
            plan, serviceProvider, RuntimePipelineKind.Workflow, "a workflow");
        Plan = plan;
    }

    public RuntimePipelinePlan Plan { get; }

    public ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate terminal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terminal);

        return RuntimeExecutionPipelineCore.Invoke(
            _middleware,
            context,
            ctx => terminal(ctx),
            (middleware, ctx, next) => middleware.InvokeAsync(ctx, pipelineContext => next(pipelineContext)));
    }
}
