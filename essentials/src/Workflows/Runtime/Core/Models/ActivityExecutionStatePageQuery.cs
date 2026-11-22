namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One finite activity-execution state page within a workflow execution.</summary>
public sealed record ActivityExecutionStatePageQuery : RuntimeStorePageRequest
{
    public ActivityExecutionStatePageQuery(
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

/// <summary>One finite page of direct children within a workflow execution.</summary>
public sealed record ActivityExecutionStateParentPageQuery : RuntimeStorePageRequest
{
    public ActivityExecutionStateParentPageQuery(
        string workflowExecutionId,
        string parentActivityExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        WorkflowExecutionId = workflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
    }

    public string WorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
}
