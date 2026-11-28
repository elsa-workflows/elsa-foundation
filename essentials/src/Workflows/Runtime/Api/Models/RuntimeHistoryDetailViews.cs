using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record RuntimeHistoryBookmarkView(ActivityExecutionBookmarkSummaryView Bookmark, string? ExecutableNodeId,
    string Status, string? CreatedHistoryEventId, string? EndedHistoryEventId, string LastHistoryEventId,
    long LastSequence, long FirstRecordedSequence, DateTimeOffset? EndedAt, string? Reason,
    string? SelectedBookmarkId, string PriorState, string Eligibility, string? EligibilityReason, DateTimeOffset EligibilityObservedAt)
{
    internal static RuntimeHistoryBookmarkView From(RuntimeHistoryBookmark item, DateTimeOffset readAt, string eligibility, string? eligibilityReason) => new(
        ActivityExecutionBookmarkSummaryView.From(item.Bookmark), item.ExecutableNodeId, item.Status.ToString(),
        item.CreatedHistoryEventId, item.EndedHistoryEventId, item.LastHistoryEventId, item.LastSequence,
        item.FirstRecordedSequence, item.EndedAt, item.Reason, item.SelectedBookmarkId, item.PriorState.ToString(),
        eligibility, eligibilityReason, readAt);
}

public sealed record RuntimeHistoryBookmarkPageView(RuntimeHistoryCoverageView Coverage,
    IReadOnlyList<RuntimeHistoryBookmarkView> Items, string? NextContinuationToken, long? TotalCount,
    long? ActiveCount, bool ScopeComplete, string? ScopeLimitation, DateTimeOffset ReadAt);

public sealed record RuntimeHistoryEventDetailsView(RuntimeHistoryCoverageView Coverage, RuntimeHistoryEventView Event,
    DateTimeOffset ReadAt);

public sealed record RuntimeHistoryBookmarkDetailsView(RuntimeHistoryCoverageView Coverage, RuntimeHistoryBookmarkView Bookmark,
    DateTimeOffset ReadAt);
