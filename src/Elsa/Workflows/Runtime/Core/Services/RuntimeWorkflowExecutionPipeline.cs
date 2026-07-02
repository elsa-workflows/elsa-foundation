using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Runs the workflow runtime middleware (in <see cref="RuntimePipelinePlan"/> order) around a terminal delegate.
/// </summary>
public sealed class RuntimeWorkflowExecutionPipeline : IRuntimeWorkflowExecutionPipeline
{
    private readonly IReadOnlyList<IWorkflowRuntimeMiddleware> _middleware;

    public RuntimeWorkflowExecutionPipeline(RuntimePipelinePlan plan, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (plan.PipelineKind != RuntimePipelineKind.Workflow)
            throw new ArgumentException($"Expected a workflow runtime pipeline plan but received '{plan.PipelineKind}'.", nameof(plan));

        Plan = plan;
        _middleware = plan.Steps
            .Select(step => (IWorkflowRuntimeMiddleware)serviceProvider.GetRequiredService(step.MiddlewareType))
            .ToArray();
    }

    public RuntimePipelinePlan Plan { get; }

    public ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate terminal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terminal);

        // Fold the ordered middleware right-to-left so the first plan step is the outermost frame and the terminal
        // (the scheduler work handler) is the innermost. With only pass-through placeholders this reduces to terminal(context).
        var next = terminal;
        for (var index = _middleware.Count - 1; index >= 0; index--)
        {
            var middleware = _middleware[index];
            var downstream = next;
            next = pipelineContext => middleware.InvokeAsync(pipelineContext, downstream);
        }

        return next(context);
    }
}
