namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A provider-issued snapshot for application authorization and a subsequent fenced delete.</summary>
public sealed record WorkflowExecutionDeletionCandidate(WorkflowExecutionState State, string ConcurrencyToken);

public enum WorkflowExecutionDeletionResult
{
    Deleted,
    NotFound,
    Changed,
    NotTerminal,
    Unsupported
}
