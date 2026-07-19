namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Thrown when a checkpoint commit tries to consume (delete) a claimed scheduler work item inside its unit-of-work but
/// the item's claim fence no longer matches — a successor reclaimed it (fencing token advanced) or already completed it
/// (WU-1 / spec 105). It reproduces the claim-lost detection the drainer's separate <c>CompleteClaimAsync</c> provided:
/// the whole commit rolls back and the drainer must neither acknowledge nor poison the item, because a successor owns it.
/// </summary>
public sealed class RuntimeSchedulerWorkConsumeConflictException : Exception
{
    public RuntimeSchedulerWorkConsumeConflictException(string workflowExecutionId, string workItemId)
        : base(
            $"Checkpoint commit for workflow execution '{workflowExecutionId}' could not consume claimed scheduler work " +
            $"item '{workItemId}': its claim was lost to a successor. The commit is rolled back and the item is left for " +
            "its current owner (claim-lost).")
    {
        WorkflowExecutionId = workflowExecutionId;
        WorkItemId = workItemId;
    }

    public string WorkflowExecutionId { get; }
    public string WorkItemId { get; }
}
