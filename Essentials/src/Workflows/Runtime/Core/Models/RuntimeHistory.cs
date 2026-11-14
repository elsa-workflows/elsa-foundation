using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeHistoryAuthority { Unspecified, CommittedTransition, DeliveryObservation }
public enum RuntimeHistoryBookmarkStatus { Active, Consumed, Removed, Superseded, Unknown }
public enum RuntimeHistoryCreationEvidence { Unknown, CreationMetadata, CreationEvent }

/// <summary>Immutable capture policy pinned before a new execution is admitted; absence means legacy uncaptured work.</summary>
public sealed record RuntimeHistoryCapturePolicy
{
    [JsonConstructor]
    public RuntimeHistoryCapturePolicy(string policyId, int version, bool required, IReadOnlyList<string> supportedKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentNullException.ThrowIfNull(supportedKinds);
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (supportedKinds.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("History kinds cannot be blank.", nameof(supportedKinds));
        PolicyId = policyId; Version = version; Required = required;
        SupportedKinds = Array.AsReadOnly(supportedKinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
    public string PolicyId { get; }
    public int Version { get; }
    public bool Required { get; }
    public IReadOnlyList<string> SupportedKinds { get; }
    public bool IsEquivalentTo(RuntimeHistoryCapturePolicy? other) => other is not null && PolicyId == other.PolicyId && Version == other.Version && Required == other.Required && SupportedKinds.SequenceEqual(other.SupportedKinds, StringComparer.Ordinal);
}

public sealed record RuntimeHistoryCausation(string? HistoryEventId = null, string? WorkflowExecutionId = null,
    string? DeliveryId = null, string? CommandId = null, string? SchedulerWorkItemId = null);

public sealed record RuntimeHistoryBookmarkMutation(ActivityExecutionBookmarkSummary Bookmark,
    RuntimeHistoryBookmarkStatus Status, string? Reason = null, string? SelectedBookmarkId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelatedActivityExecutionId { get; init; }
}

/// <summary>A deterministic unpositioned event; only the atomic writer assigns its durable position.</summary>
public sealed record RuntimeHistoryCandidate
{
    [JsonConstructor]
    public RuntimeHistoryCandidate(RuntimeHistoryEvent @event, int checkpointOrdinal, RuntimeHistoryBookmarkMutation? bookmarkMutation = null)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.Sequence != 0 || @event.PhysicalCommitId is not null || @event.RecordedAt is not null)
            throw new ArgumentException("A history candidate cannot carry a provider-assigned position.", nameof(@event));
        if (checkpointOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(checkpointOrdinal));
        if (@event.Payload is not null) throw new ArgumentException("Semantic history references protected values instead of copying payloads.", nameof(@event));
        if (bookmarkMutation is { } mutation && (mutation.Bookmark.Identity.WorkflowExecutionId != @event.WorkflowExecutionId ||
            mutation.Bookmark.Identity.ActivityExecutionId != @event.ActivityExecutionId || mutation.Bookmark.Identity.BookmarkId != @event.BookmarkId))
            throw new ArgumentException("Bookmark history must agree with its event owner and handle.", nameof(bookmarkMutation));
        Event = @event; CheckpointOrdinal = checkpointOrdinal; BookmarkMutation = bookmarkMutation;
    }
    public RuntimeHistoryEvent Event { get; }
    public int CheckpointOrdinal { get; }
    public RuntimeHistoryBookmarkMutation? BookmarkMutation { get; }
}

public sealed record RuntimeHistoryBatch
{
    [JsonConstructor]
    public RuntimeHistoryBatch(RuntimeHistoryCapturePolicy policy, string logicalCheckpointId, DateTimeOffset occurredAt,
        bool startsAtRunStart, IReadOnlyList<RuntimeHistoryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentException.ThrowIfNullOrWhiteSpace(logicalCheckpointId); ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Select(candidate => candidate.Event.WorkflowExecutionId).Distinct(StringComparer.Ordinal).Skip(1).Any() ||
            candidates.Any(candidate => candidate.Event.CheckpointId != logicalCheckpointId) ||
            !candidates.Select(candidate => candidate.CheckpointOrdinal).SequenceEqual(Enumerable.Range(0, candidates.Count)) ||
            candidates.Select(candidate => candidate.Event.HistoryEventId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new ArgumentException("History candidates require one workflow/checkpoint and unique identities/ordinals.", nameof(candidates));
        Policy = policy; LogicalCheckpointId = logicalCheckpointId; OccurredAt = occurredAt; StartsAtRunStart = startsAtRunStart; Candidates = Array.AsReadOnly(candidates.ToArray());
        if (Candidates.Count > 0) RuntimeHistorySafety.Validate(this, Candidates[0].Event.WorkflowExecutionId);
    }
    public RuntimeHistoryCapturePolicy Policy { get; }
    public string LogicalCheckpointId { get; }
    public DateTimeOffset OccurredAt { get; }
    public bool StartsAtRunStart { get; }
    public IReadOnlyList<RuntimeHistoryCandidate> Candidates { get; }
    /// <summary>Optional proof of the unchanged logical checkpoint source, excluding its history contribution.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFingerprint { get; init; }
}

public sealed record RuntimeHistoryHead(string WorkflowExecutionId, long FirstSequence, long LastSequence,
    string LastPhysicalCommitId, DateTimeOffset LastCommittedAt, RuntimeHistoryCapturePolicy Policy,
    bool CapturedFromStart, bool HasGaps, long PurgedThroughSequence);

public sealed record RuntimeHistoryBookmark(ActivityExecutionBookmarkSummary Bookmark, string? ExecutableNodeId,
    RuntimeHistoryBookmarkStatus Status, string? CreatedHistoryEventId, string? EndedHistoryEventId,
    string LastHistoryEventId, long LastSequence, long FirstRecordedSequence, DateTimeOffset? EndedAt,
    string? Reason, string? SelectedBookmarkId, RuntimeHistoryCreationEvidence PriorState)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupersededByActivityExecutionId { get; init; }
}

/// <summary>Immutable filters; AccessBinding is caller authorization context for token binding, never authority.</summary>
public sealed record RuntimeHistoryQuery : RuntimeStorePageRequest
{
    public RuntimeHistoryQuery(string workflowExecutionId, int limit = DefaultLimit, string? continuationToken = null,
        string? activityExecutionId = null, bool includeDescendants = false, string? bookmarkId = null,
        RuntimeHistoryEventCategory? category = null, string? name = null, string? search = null, string? accessBinding = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (search?.Length > 256) throw new ArgumentException("History search is limited to 256 characters.", nameof(search));
        if (includeDescendants && activityExecutionId is null) throw new ArgumentException("Descendant scope requires an activity execution.", nameof(includeDescendants));
        WorkflowExecutionId = workflowExecutionId; ActivityExecutionId = activityExecutionId; IncludeDescendants = includeDescendants;
        BookmarkId = bookmarkId; Category = category; Name = name; Search = search; AccessBinding = accessBinding;
    }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public bool IncludeDescendants { get; }
    public string? BookmarkId { get; }
    public RuntimeHistoryEventCategory? Category { get; }
    public string? Name { get; }
    public string? Search { get; }
    public string? AccessBinding { get; }
}

/// <summary>A bounded scanned window may contain no matches and still expose an advancing continuation.</summary>
public sealed record RuntimeHistoryPage(RuntimeHistoryHead? Head, IReadOnlyList<RuntimeHistoryEvent> Items,
    long UpperSequence, long ScannedThroughSequence, int ScannedCount, string? NextContinuationToken, string CatchUpToken)
{
    public bool ScopeComplete { get; init; } = true;
    public string? ScopeLimitation { get; init; }
}

public sealed record RuntimeHistoryBookmarkQuery : RuntimeStorePageRequest
{
    public RuntimeHistoryBookmarkQuery(string workflowExecutionId, int limit = DefaultLimit, string? continuationToken = null,
        string? activityExecutionId = null, bool includeDescendants = false, RuntimeHistoryBookmarkStatus? status = null,
        string? accessBinding = null) : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (includeDescendants && activityExecutionId is null) throw new ArgumentException("Descendant scope requires an activity execution.", nameof(includeDescendants));
        WorkflowExecutionId = workflowExecutionId; ActivityExecutionId = activityExecutionId; IncludeDescendants = includeDescendants; Status = status; AccessBinding = accessBinding;
    }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public bool IncludeDescendants { get; }
    public RuntimeHistoryBookmarkStatus? Status { get; }
    public string? AccessBinding { get; }
}

public sealed record RuntimeHistoryBookmarkPage(RuntimeHistoryHead? Head, IReadOnlyList<RuntimeHistoryBookmark> Items,
    string? NextContinuationToken, long? TotalCount = null, long? ActiveCount = null)
{
    public bool ScopeComplete { get; init; } = true;
    public string? ScopeLimitation { get; init; }
}

public sealed record RuntimeHistoryObservation(RuntimeHistoryCapturePolicy Policy, RuntimeHistoryEvent Event);
public sealed record RuntimeHistoryObservationResult(bool Recorded, RuntimeHistoryEvent? Event);
