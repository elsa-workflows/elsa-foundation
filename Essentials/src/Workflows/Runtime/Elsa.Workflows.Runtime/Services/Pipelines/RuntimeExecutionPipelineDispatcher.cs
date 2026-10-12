using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Pipelines;

/// <summary>
/// Dispatches a drained scheduler work item through the runtime execution pipeline for its kind, staging the selected
/// work handler in the pipeline's <c>Invoke</c> slot so it runs before the later slots that apply its results (ADR 0029,
/// Move 2). The pipeline terminal is a guard that fails loudly if the handler was never run. It also stages the
/// partition of the DI scope it lives in, which is the command's partition, so a handler that creates its own scope
/// works in the partition the work item belongs to rather than in the host's.
/// </summary>
public sealed class RuntimeExecutionPipelineDispatcher : IRuntimeExecutionPipelineDispatcher
{
    private readonly IRuntimeSchedulerPipelineSelector _selector;
    private readonly IRuntimeWorkflowExecutionPipeline _workflowPipeline;
    private readonly IRuntimeActivityExecutionPipeline _activityPipeline;
    private readonly IPersistenceAccessContextAccessor _persistenceAccessContextAccessor;

    public RuntimeExecutionPipelineDispatcher(
        IRuntimeSchedulerPipelineSelector selector,
        IRuntimeWorkflowExecutionPipeline workflowPipeline,
        IRuntimeActivityExecutionPipeline activityPipeline,
        IPersistenceAccessContextAccessor persistenceAccessContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(workflowPipeline);
        ArgumentNullException.ThrowIfNull(activityPipeline);
        ArgumentNullException.ThrowIfNull(persistenceAccessContextAccessor);

        _selector = selector;
        _workflowPipeline = workflowPipeline;
        _activityPipeline = activityPipeline;
        _persistenceAccessContextAccessor = persistenceAccessContextAccessor;
    }

    public ValueTask DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        IWorkflowSchedulerWorkHandler handler,
        IServiceProvider? ambientServices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(handler);

        // Activity pipeline (ADR 0029, Move 2): stage the handler invocation for the Invoke slot to run before the
        // Checkpoint slot, mirroring the workflow path. Context-aware (migrated) handlers get the context explicitly and
        // stage their results; other handlers run their plain path unchanged. The terminal is a guard: reaching it with
        // the handler still staged means the Invoke slot was missing from the plan, so fail loudly.
        if (_selector.Select(workItem) == RuntimePipelineKind.Activity)
        {
            var activityContext = new ActivityRuntimePipelineContext(workItem);
            activityContext.Workspace.CancellationToken = cancellationToken;
            activityContext.Workspace.AmbientServices = ambientServices;
            activityContext.Workspace.PersistenceScope = _persistenceAccessContextAccessor.Current.Scope;
            activityContext.Workspace.InvokeHandler = pipelineContext => handler is IRuntimePipelineWorkHandler pipelineAwareHandler
                ? pipelineAwareHandler.HandleAsync(workItem, pipelineContext, cancellationToken)
                : handler.HandleAsync(workItem, cancellationToken);

            return _activityPipeline.InvokeAsync(activityContext, static pipelineContext =>
            {
                if (pipelineContext.Workspace.InvokeHandler is not null)
                    throw new InvalidOperationException(
                        $"The activity runtime pipeline completed without running the staged handler for work item '{pipelineContext.WorkItem.WorkItemId}': the '{RuntimeActivityPipelineSlots.Invoke}' slot middleware is missing from the plan.");

                return ValueTask.CompletedTask;
            });
        }

        // Workflow pipeline (ADR 0029, Move 2): stage the handler invocation for the Invoke slot to run before the
        // Checkpoint slot. Context-aware (migrated) handlers get the context explicitly and stage their results; other
        // handlers run their plain path unchanged. The terminal is a guard: if it is reached with the handler still
        // staged, the Invoke slot was missing from the plan and the handler would silently not run — so fail loudly.
        var workflowContext = new WorkflowRuntimePipelineContext(workItem);
        workflowContext.Workspace.CancellationToken = cancellationToken;
        workflowContext.Workspace.AmbientServices = ambientServices;
        workflowContext.Workspace.PersistenceScope = _persistenceAccessContextAccessor.Current.Scope;
        workflowContext.Workspace.InvokeHandler = pipelineContext => handler is IRuntimePipelineWorkHandler pipelineAwareHandler
            ? pipelineAwareHandler.HandleAsync(workItem, pipelineContext, cancellationToken)
            : handler.HandleAsync(workItem, cancellationToken);

        return _workflowPipeline.InvokeAsync(workflowContext, static pipelineContext =>
        {
            if (pipelineContext.Workspace.InvokeHandler is not null)
                throw new InvalidOperationException(
                    $"The workflow runtime pipeline completed without running the staged handler for work item '{pipelineContext.WorkItem.WorkItemId}': the '{RuntimeWorkflowPipelineSlots.Invoke}' slot middleware is missing from the plan.");

            return ValueTask.CompletedTask;
        });
    }
}
