namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record ActivityExecutionBrowseEntryView(ActivityExecutionInspectionSummaryView Execution,
    long Position, string? PreviousActivityExecutionId, string? NextActivityExecutionId, ActivityExecutionAttemptView? Attempt, long? IterationPosition);

public sealed record ActivityExecutionBrowsePageView(IReadOnlyList<ActivityExecutionBrowseEntryView> Items,
    long TotalCount, string? PreviousCursor, string? NextCursor);
