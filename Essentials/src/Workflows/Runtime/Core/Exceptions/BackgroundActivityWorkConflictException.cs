namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A background activity work admission, claim, receipt or acceptance lost its lifecycle compare-and-set.</summary>
public sealed class BackgroundActivityWorkConflictException(string workflowExecutionId, string workId, string reason)
    : Exception($"Background activity work '{workId}' in workflow execution '{workflowExecutionId}' could not be changed: {reason}.")
{
    public string WorkflowExecutionId { get; } = workflowExecutionId;
    public string WorkId { get; } = workId;
}
