namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Retained history cannot be interpreted without inventing or omitting semantic facts.</summary>
public sealed class RuntimeHistoryIntegrityException : Exception
{
    public RuntimeHistoryIntegrityException(string workflowExecutionId, string reason)
        : this(workflowExecutionId, reason, null) { }

    /// <summary>Preserves the failure that made retained history unreadable without changing its hard integrity classification.</summary>
    public RuntimeHistoryIntegrityException(string workflowExecutionId, string reason, Exception? innerException)
        : base($"History for workflow execution '{workflowExecutionId}' is inconsistent: {reason}", innerException) { }
}
