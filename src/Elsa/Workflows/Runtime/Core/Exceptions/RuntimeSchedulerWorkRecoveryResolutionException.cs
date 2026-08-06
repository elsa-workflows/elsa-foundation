namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Represents a provider failure while resolving durable scheduler work for checkpoint recovery.</summary>
public sealed class RuntimeSchedulerWorkRecoveryResolutionException : Exception
{
    public RuntimeSchedulerWorkRecoveryResolutionException(
        string workflowExecutionId,
        string workItemId,
        Exception innerException)
        : base(
            $"Failed to resolve durable scheduler work item '{workItemId}' for workflow execution '{workflowExecutionId}'.",
            innerException)
    {
        WorkflowExecutionId = workflowExecutionId;
        WorkItemId = workItemId;
    }

    public string WorkflowExecutionId { get; }
    public string WorkItemId { get; }
}
