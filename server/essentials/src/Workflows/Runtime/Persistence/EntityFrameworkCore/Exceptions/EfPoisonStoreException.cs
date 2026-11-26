namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>
/// Boundary exception of the EF scheduler-poison store's fenced record, as its sibling stores have theirs; provider
/// exception types do not cross it.
/// </summary>
public sealed class EfPoisonStoreException(
    string operation,
    string identity,
    string message,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    /// <summary>What the store was doing when the provider failed, as a phrase that completes "failed while".</summary>
    public string Operation { get; } = operation;

    /// <summary>The record the operation was for, as <c>WorkflowExecutionId/WorkItemId</c>.</summary>
    public string Identity { get; } = identity;
}
