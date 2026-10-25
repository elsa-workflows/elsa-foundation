namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A retained called-workflow control boundary could not be resolved safely.</summary>
public sealed class WorkflowControlBoundaryException : InvalidOperationException
{
    public WorkflowControlBoundaryException(string workflowExecutionId, string? rootActivityExecutionId, Exception innerException)
        : base(rootActivityExecutionId is null
            ? $"Could not resolve control boundary for workflow '{workflowExecutionId}' before identifying its root."
            : $"Could not resolve control boundary for workflow '{workflowExecutionId}' and root '{rootActivityExecutionId}'.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (rootActivityExecutionId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(rootActivityExecutionId);
        ArgumentNullException.ThrowIfNull(innerException);
        WorkflowExecutionId = workflowExecutionId;
        RootActivityExecutionId = rootActivityExecutionId;
    }

    public string WorkflowExecutionId { get; }
    /// <summary>The exact root invocation when it could be identified, otherwise null.</summary>
    public string? RootActivityExecutionId { get; }
}
