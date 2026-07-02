using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Dispatches a drained scheduler work item through the runtime execution pipeline for its kind, with the selected
/// work handler as the pipeline's inner terminal delegate (ADR 0029, Move 1 — "wrap, do not replace").
/// </summary>
public sealed class RuntimeExecutionPipelineDispatcher : IRuntimeExecutionPipelineDispatcher
{
    private readonly IRuntimeSchedulerPipelineSelector _selector;
    private readonly IRuntimeWorkflowExecutionPipeline _workflowPipeline;
    private readonly IRuntimeActivityExecutionPipeline _activityPipeline;
    private readonly IRuntimePipelineContextAccessor _pipelineContextAccessor;

    public RuntimeExecutionPipelineDispatcher(
        IRuntimeSchedulerPipelineSelector selector,
        IRuntimeWorkflowExecutionPipeline workflowPipeline,
        IRuntimeActivityExecutionPipeline activityPipeline,
        IRuntimePipelineContextAccessor? pipelineContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(workflowPipeline);
        ArgumentNullException.ThrowIfNull(activityPipeline);

        _selector = selector;
        _workflowPipeline = workflowPipeline;
        _activityPipeline = activityPipeline;
        _pipelineContextAccessor = pipelineContextAccessor ?? NoopRuntimePipelineContextAccessor.Instance;
    }

    public async ValueTask DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        IWorkflowSchedulerWorkHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(handler);

        // Push the context so the terminal handler can reach the shared workspace (it is invoked as a bare delegate and
        // gets no context by parameter). The scope spans the whole pipeline invocation, including the handler.
        if (_selector.Select(workItem) == RuntimePipelineKind.Activity)
        {
            var activityContext = new ActivityRuntimePipelineContext(workItem);
            using (_pipelineContextAccessor.Push(activityContext))
                await _activityPipeline.InvokeAsync(activityContext, _ => handler.HandleAsync(workItem, cancellationToken));
            return;
        }

        var workflowContext = new WorkflowRuntimePipelineContext(workItem);
        using (_pipelineContextAccessor.Push(workflowContext))
            await _workflowPipeline.InvokeAsync(workflowContext, _ => handler.HandleAsync(workItem, cancellationToken));
    }
}
