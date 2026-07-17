namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Signals that an admitted child has not yet acknowledged deterministic cancellation.</summary>
public sealed class ChildCancelDeferredException : Exception
{
    public ChildCancelDeferredException(string dispatchId, string childWorkflowExecutionId)
        : base($"DispatchWorkflow child cancellation for dispatch '{dispatchId}' is not yet terminal on child '{childWorkflowExecutionId}'.")
    {
        DispatchId = dispatchId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
    }

    public string DispatchId { get; }
    public string ChildWorkflowExecutionId { get; }
}
