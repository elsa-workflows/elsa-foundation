namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeHistoryHeadEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public long PurgedThroughSequence { get; set; }
}

internal sealed class RuntimeHistoryEventEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string HistoryEventId { get; set; } = null!;
    public string HistoryEventIdHash { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameHash { get; set; } = null!;
    public int Category { get; set; }
    public string CandidateFingerprint { get; set; } = null!;
    public bool IsPurged { get; set; }
    public string? ActivityExecutionId { get; set; }
    public string? ActivityExecutionIdHash { get; set; }
    public string? BookmarkId { get; set; }
    public string? BookmarkIdHash { get; set; }
    public long Sequence { get; set; }
}

internal sealed class RuntimeHistoryBookmarkEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string BookmarkId { get; set; } = null!;
    public string BookmarkIdHash { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public long FirstRecordedSequence { get; set; }
    public int Status { get; set; }
}

internal sealed class RuntimeHistoryBookmarkCountEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string? ActivityExecutionId { get; set; }
    public string ActivityExecutionIdHash { get; set; } = null!;
    public long TotalCount { get; set; }
    public long ActiveCount { get; set; }
}

internal sealed class RuntimeHistoryActivityEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string? ParentActivityExecutionId { get; set; }
}
