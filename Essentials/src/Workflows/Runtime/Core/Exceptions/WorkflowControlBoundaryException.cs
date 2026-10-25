namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A retained called-workflow control boundary could not be resolved safely.</summary>
public sealed class WorkflowControlBoundaryException : InvalidOperationException
{
    public WorkflowControlBoundaryException(string workflowExecutionId, string rootActivityExecutionId, Exception innerException)
        : base($"Could not resolve control boundary for workflow '{workflowExecutionId}' and root '{rootActivityExecutionId}'.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootActivityExecutionId);
        ArgumentNullException.ThrowIfNull(innerException);
        WorkflowExecutionId = workflowExecutionId;
        RootActivityExecutionId = rootActivityExecutionId;
    }

    public string WorkflowExecutionId { get; }
    public string RootActivityExecutionId { get; }
}
