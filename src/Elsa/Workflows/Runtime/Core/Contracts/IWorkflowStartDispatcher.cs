using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches workflow execution starts through the workflow execution agent boundary.
/// </summary>
public interface IWorkflowStartDispatcher
{
    /// <param name="dispatchOptions">
    /// Optional per-request dispatch options forwarded verbatim to the workflow execution agent (spec 089 FR-019).
    /// It carries the ambient request scope the in-process inline drain uses to build activity execution contexts.
    /// <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/> (identical to the pre-089 single-arg behavior).
    /// </param>
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);

    /// <param name="dispatchOptions">See <see cref="DispatchAsync"/>. <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/>.</param>
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchTransientAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);
}
