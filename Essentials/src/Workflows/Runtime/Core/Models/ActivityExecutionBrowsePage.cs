namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A bounded, live-view query over committed activity execution evidence.</summary>
public sealed record ActivityExecutionBrowseQuery : RuntimeStorePageRequest
{
    public ActivityExecutionBrowseQuery(string workflowExecutionId, int limit = 50, long? offset = null,
        string? executableNodeId = null, string? parentActivityExecutionId = null,
        string? ancestorActivityExecutionId = null, string? anchorActivityExecutionId = null) : base(limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset is not null && anchorActivityExecutionId is not null)
            throw new ArgumentException("Choose either a page offset or an execution anchor.");
        foreach (var value in new[] { executableNodeId, parentActivityExecutionId, ancestorActivityExecutionId, anchorActivityExecutionId })
            if (value is not null) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        WorkflowExecutionId = workflowExecutionId;
        Offset = offset;
        ExecutableNodeId = executableNodeId;
        ParentActivityExecutionId = parentActivityExecutionId;
        AncestorActivityExecutionId = ancestorActivityExecutionId;
        AnchorActivityExecutionId = anchorActivityExecutionId;
    }

    public string WorkflowExecutionId { get; }
    public long? Offset { get; }
    public string? ExecutableNodeId { get; }
    public string? ParentActivityExecutionId { get; }
    public string? AncestorActivityExecutionId { get; }
    public string? AnchorActivityExecutionId { get; }
}

/// <summary>Position and adjacent identities in the entire matching collection, before pagination.</summary>
public sealed record ActivityExecutionBrowseEntry(ActivityExecutionInspectionSummaryProjection Execution,
    long Position, string? PreviousActivityExecutionId, string? NextActivityExecutionId, long? IterationPosition = null);

/// <summary>Count and bounded entries observed in one consistent read; no cross-request snapshot is promised.</summary>
public sealed record ActivityExecutionBrowsePage(long TotalCount, long Offset,
    IReadOnlyList<ActivityExecutionBrowseEntry> Items);
