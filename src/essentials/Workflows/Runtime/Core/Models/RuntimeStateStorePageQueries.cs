namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One finite, forward-only page of bookmark state for a workflow execution.
/// </summary>
public sealed record BookmarkStatePageQuery : RuntimeStorePageRequest
{
    public BookmarkStatePageQuery(
        string workflowExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
    }

    public string WorkflowExecutionId { get; }
}

/// <summary>
/// One finite, forward-only page of durable value state for a workflow execution.
/// </summary>
public sealed record DurableValueStatePageQuery : RuntimeStorePageRequest
{
    public DurableValueStatePageQuery(
        string workflowExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        WorkflowExecutionId = workflowExecutionId;
    }

    public string WorkflowExecutionId { get; }
}
