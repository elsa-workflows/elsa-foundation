namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One finite page of activity-execution inspection summaries within a workflow execution.</summary>
public sealed record ActivityExecutionInspectionSummaryPageQuery : RuntimeStorePageRequest
{
    public ActivityExecutionInspectionSummaryPageQuery(
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
/// A bounded summary page plus the exact predicate count needed by workflow-instance inspection.
/// </summary>
public sealed record ActivityExecutionInspectionSummaryPage
{
    public ActivityExecutionInspectionSummaryPage(
        ActivityExecutionInspectionSummaryPageQuery query,
        IReadOnlyList<ActivityExecutionInspectionSummaryProjection> items,
        long totalCount,
        string? nextContinuationToken = null)
    {
        Page = new RuntimeStorePage<ActivityExecutionInspectionSummaryProjection>(
            query,
            items,
            nextContinuationToken);
        if (totalCount < items.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCount),
                totalCount,
                "The activity-summary total count cannot be smaller than the returned page.");
        }

        TotalCount = totalCount;
    }

    public RuntimeStorePage<ActivityExecutionInspectionSummaryProjection> Page { get; }
    public IReadOnlyList<ActivityExecutionInspectionSummaryProjection> Items => Page.Items;
    public string? NextContinuationToken => Page.NextContinuationToken;
    public long TotalCount { get; }
}
