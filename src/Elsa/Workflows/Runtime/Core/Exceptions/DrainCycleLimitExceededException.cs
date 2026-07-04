namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class DrainCycleLimitExceededException : InvalidOperationException
{
    public DrainCycleLimitExceededException(string workflowExecutionId, int maxDrainCycles)
        : base($"Workflow execution drain exceeded the maximum cycle count of {maxDrainCycles} for workflow execution '{workflowExecutionId}'.")
    {
        WorkflowExecutionId = workflowExecutionId;
        MaxDrainCycles = maxDrainCycles;
    }

    public string WorkflowExecutionId { get; }
    public int MaxDrainCycles { get; }
}
