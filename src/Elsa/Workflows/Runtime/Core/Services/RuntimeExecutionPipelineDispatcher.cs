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

    public RuntimeExecutionPipelineDispatcher(
        IRuntimeSchedulerPipelineSelector selector,
        IRuntimeWorkflowExecutionPipeline workflowPipeline,
        IRuntimeActivityExecutionPipeline activityPipeline)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(workflowPipeline);
        ArgumentNullException.ThrowIfNull(activityPipeline);

        _selector = selector;
        _workflowPipeline = workflowPipeline;
        _activityPipeline = activityPipeline;
    }

    public ValueTask DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        IWorkflowSchedulerWorkHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(handler);

        // Activity pipeline: the handler still runs as the pipeline terminal (not yet decomposed into the Invoke slot).
        if (_selector.Select(workItem) == RuntimePipelineKind.Activity)
            return _activityPipeline.InvokeAsync(
                new ActivityRuntimePipelineContext(workItem),
                _ => handler.HandleAsync(workItem, cancellationToken));

        // Workflow pipeline (ADR 0029, Move 2): stage the handler invocation for the Invoke slot to run before the
        // Checkpoint slot; the terminal is a no-op. Context-aware (migrated) handlers get the context explicitly and
        // stage their results; other handlers run their plain path unchanged.
        var workflowContext = new WorkflowRuntimePipelineContext(workItem);
        workflowContext.Workspace.InvokeHandler = pipelineContext => handler is IRuntimePipelineWorkHandler pipelineAwareHandler
            ? pipelineAwareHandler.HandleAsync(workItem, pipelineContext, cancellationToken)
            : handler.HandleAsync(workItem, cancellationToken);

        return _workflowPipeline.InvokeAsync(workflowContext, _ => ValueTask.CompletedTask);
    }
}
