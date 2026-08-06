namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Raised when a durable Prepared frontier cannot be recovered safely before source dispatch.</summary>
public sealed class RuntimeCheckpointPreparedRecoveryException : InvalidOperationException
{
    public RuntimeCheckpointPreparedRecoveryException(string workflowExecutionId, string reason)
        : base($"Prepared checkpoint recovery for workflow execution '{workflowExecutionId}' failed closed: {reason}")
    {
        WorkflowExecutionId = workflowExecutionId;
        Reason = reason;
    }

    public string WorkflowExecutionId { get; }
    public string Reason { get; }
}
