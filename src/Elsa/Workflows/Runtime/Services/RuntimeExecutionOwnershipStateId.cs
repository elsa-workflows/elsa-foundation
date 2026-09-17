using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

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

    /// <summary>
    /// Refuses a checkpoint write to any execution's ownership record. That record is reserved to the ownership service,
    /// which changes it only by compare-and-swap; a checkpoint that wrote it would bypass the fence it is checked against.
    /// Stores apply this before any write because protecting a reserved key is storage integrity, not a commit rule.
    /// </summary>
    public static void EnsureNotWritten(IEnumerable<RuntimeStateChange<ExecutionLivenessState>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Any(change => StringComparer.Ordinal.Equals(change.State.OperationalStateId, For(change.State.WorkflowExecutionId))))
            throw new RuntimeCheckpointCommitValidationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
    }
}
