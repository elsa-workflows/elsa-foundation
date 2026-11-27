namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A provider could not complete an operation on the selected run's diagnostic history.</summary>
/// <remarks>A failed write may have committed. Reconcile its stable identity before retrying; this exception is not rollback proof.</remarks>
public sealed class RuntimeHistoryUnavailableException : Exception
{
    public RuntimeHistoryUnavailableException(string workflowExecutionId, string operation, Exception innerException)
        : base($"History operation '{operation}' for workflow execution '{workflowExecutionId}' could not complete.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(innerException);
        WorkflowExecutionId = workflowExecutionId;
        Operation = operation;
    }

    public string WorkflowExecutionId { get; }
    public string Operation { get; }
}
