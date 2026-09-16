namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The one definition of the reserved execution-liveness state ID that holds a workflow execution's single-writer
/// ownership lease and its highest issued fencing token. Every store and service that reads, writes, fences, or protects
/// that record derives the ID here.
/// </summary>
/// <remarks>The ID is persisted, so its format must never change.</remarks>
public static class RuntimeExecutionOwnershipStateId
{
    public static string For(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return $"ownership:{workflowExecutionId}";
    }
}
