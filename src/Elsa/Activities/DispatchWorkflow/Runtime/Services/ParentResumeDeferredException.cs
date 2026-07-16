namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Safe retry classification for committed resume work whose bookmark is not yet consumed.</summary>
public sealed class ParentResumeDeferredException : Exception
{
    public ParentResumeDeferredException(string dispatchId, string parentWorkflowExecutionId)
        : base($"DispatchWorkflow parent resume for dispatch '{dispatchId}' is not yet consumed by parent '{parentWorkflowExecutionId}'.")
    {
        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
}
