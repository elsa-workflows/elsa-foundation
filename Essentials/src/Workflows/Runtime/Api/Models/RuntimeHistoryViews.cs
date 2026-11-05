using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>Coverage describes retained evidence, independently of the run's current lifecycle status.</summary>
public sealed record RuntimeHistoryCoverageView(string State, string? Limitation, string? PolicyId, int? PolicyVersion,
    IReadOnlyList<string> SupportedKinds, bool CapturedFromStart, bool HasGaps, long FirstSequence,
    long LastSequence, long SettledSequence, long PurgedThroughSequence, string? LastPhysicalCommitId,
    DateTimeOffset? LastCommittedAt);

public sealed record RuntimeHistoryPageView(RuntimeHistoryCoverageView Coverage, IReadOnlyList<RuntimeHistoryEventView> Items,
    long UpperSequence, long ScannedThroughSequence, int ScannedCount, string? NextContinuationToken,
    string? CatchUpToken, bool ScopeComplete, string? ScopeLimitation, DateTimeOffset ReadAt);

public sealed record RuntimeHistoryEventView(string HistoryEventId, string WorkflowExecutionId, long Sequence,
    int SchemaVersion, string Name, string Category, string Authority, DateTimeOffset OccurredAt,
    DateTimeOffset? RecordedAt, string? ActivityExecutionId, string? ExecutableNodeId, string? CheckpointId,
    int? CheckpointOrdinal, string? PhysicalCommitId, string? BookmarkId, string? IncidentId, string? AttemptId,
    string? ParentActivityExecutionId, string? ExecutionScopeId, string? IterationId,
    RuntimeHistoryCausation? Causation, IReadOnlyDictionary<string, string> Metadata)
{
    internal static RuntimeHistoryEventView From(RuntimeHistoryEvent item) => new(item.HistoryEventId,
        item.WorkflowExecutionId, item.Sequence, item.SchemaVersion, item.Name, item.Category.ToString(),
        item.Authority.ToString(), item.OccurredAt, item.RecordedAt, item.ActivityExecutionId, item.ExecutableNodeId,
        item.CheckpointId, item.CheckpointOrdinal, item.PhysicalCommitId, item.BookmarkId, item.IncidentId,
        item.AttemptId, item.ParentActivityExecutionId, item.ExecutionScopeId, item.IterationId, item.Causation,
        item.Metadata.Where(pair => SafeMetadata.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    // Keep unknown event kinds readable without returning arbitrary historical metadata or the legacy Payload slot.
    private static readonly HashSet<string> SafeMetadata = new(StringComparer.Ordinal)
    {
        "history.status", "history.reasonCode", "history.outcomeNames", "history.stimulusType", "history.resumeTargetId",
        "history.selectedBookmarkId", "history.childWorkflowId", "history.timerId", "history.scheduledAt", "history.controlKind",
        "history.valueEvidenceIds", "history.incidentFailureType", "history.originalCheckpoint", "history.capturePolicyId"
    };
}

public sealed record RuntimeHistoryBookmarkView(ActivityExecutionBookmarkSummaryView Bookmark, string? ExecutableNodeId,
    string Status, string? CreatedHistoryEventId, string? EndedHistoryEventId, string LastHistoryEventId,
    long LastSequence, long FirstRecordedSequence, DateTimeOffset? EndedAt, string? Reason,
    string? SelectedBookmarkId, string PriorState, string Eligibility, DateTimeOffset EligibilityObservedAt)
{
    internal static RuntimeHistoryBookmarkView From(RuntimeHistoryBookmark item, DateTimeOffset readAt, string eligibility) => new(
        ActivityExecutionBookmarkSummaryView.From(item.Bookmark), item.ExecutableNodeId, item.Status.ToString(),
        item.CreatedHistoryEventId, item.EndedHistoryEventId, item.LastHistoryEventId, item.LastSequence,
        item.FirstRecordedSequence, item.EndedAt, item.Reason, item.SelectedBookmarkId, item.PriorState.ToString(),
        eligibility, readAt);
}

public sealed record RuntimeHistoryBookmarkPageView(RuntimeHistoryCoverageView Coverage,
    IReadOnlyList<RuntimeHistoryBookmarkView> Items, string? NextContinuationToken, long? TotalCount,
    long? ActiveCount, bool ScopeComplete, string? ScopeLimitation, DateTimeOffset ReadAt);

public sealed record RuntimeHistoryEventDetailsView(RuntimeHistoryCoverageView Coverage, RuntimeHistoryEventView Event,
    DateTimeOffset ReadAt);

public sealed record RuntimeHistoryBookmarkDetailsView(RuntimeHistoryCoverageView Coverage, RuntimeHistoryBookmarkView Bookmark,
    DateTimeOffset ReadAt);
