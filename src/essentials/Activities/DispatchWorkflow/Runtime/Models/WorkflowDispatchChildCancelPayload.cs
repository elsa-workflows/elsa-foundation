using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>Stable identifiers required to cancel one admitted DispatchWorkflow child.</summary>
public sealed class WorkflowDispatchChildCancelPayload
{
    public WorkflowDispatchChildCancelPayload(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);

        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(dispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(childWorkflowExecutionId, identity.ChildWorkflowExecutionId))
        {
            throw new ArgumentException("Child-cancel payload does not match its deterministic dispatch identity.");
        }

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
}
