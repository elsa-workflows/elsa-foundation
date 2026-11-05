namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Retained history cannot be interpreted without inventing or omitting semantic facts.</summary>
public sealed class RuntimeHistoryIntegrityException(string workflowExecutionId, string reason)
    : Exception($"History for workflow execution '{workflowExecutionId}' is inconsistent: {reason}");
