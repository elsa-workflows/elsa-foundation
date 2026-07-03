using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches workflow execution starts through the workflow execution agent boundary.
/// </summary>
public interface IWorkflowStartDispatcher
{
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutionStartDispatchResult> DispatchTransientAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutable executable,
        CancellationToken cancellationToken = default);
}
