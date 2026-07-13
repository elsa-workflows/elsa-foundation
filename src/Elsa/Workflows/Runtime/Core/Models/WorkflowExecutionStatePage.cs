using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Direction of travel from a stable workflow-execution history anchor.</summary>
public enum WorkflowExecutionStatePageDirection
{
    Previous,
    Next
}

/// <summary>
/// Stable keyset anchor returned by <see cref="WorkflowExecutionStatePage"/>. Cursors are bound to the
/// complete filter scope that produced them. The embedded page size preserves backward-navigation behavior
/// while callers remain free to change the requested size on a later page.
/// </summary>
public sealed record WorkflowExecutionStatePageCursor(
    DateTimeOffset SortTimestamp,
    string WorkflowExecutionId,
    WorkflowExecutionStatePageDirection Direction,
    int PageSize,
    string Scope);

/// <summary>Provider-neutral, bounded workflow-execution history query.</summary>
public sealed record WorkflowExecutionStatePageQuery(
    int PageSize,
    string? TenantId = null,
    string? DefinitionId = null,
    WorkflowExecutionStatus? Status = null,
    WorkflowRunKind? RunKind = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? CorrelationId = null,
    string? WorkflowExecutionId = null,
    string? ArtifactId = null,
    WorkflowExecutionStatePageCursor? Cursor = null)
{
    /// <summary>Validates the bounded request and cursor binding.</summary>
    public void Validate()
    {
        if (PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "Page size must be greater than zero.");
        if (From is { } from && To is { } to && from > to)
            throw new ArgumentException("The workflow execution history start must not be after its end.", nameof(From));
        if (Cursor is not { } cursor)
            return;
        if (!StringComparer.Ordinal.Equals(cursor.Scope, Scope()))
            throw new ArgumentException("The workflow execution history cursor does not belong to this query.", nameof(Cursor));
    }

    /// <summary>Returns a deterministic fingerprint over every filter that defines this result set.</summary>
    public string Scope()
    {
        var json = JsonSerializer.Serialize(new object?[]
        {
            TenantId,
            DefinitionId,
            Status is { } status ? (int)status : null,
            RunKind is { } runKind ? (int)runKind : null,
            From?.UtcTicks,
            To?.UtcTicks,
            CorrelationId,
            WorkflowExecutionId,
            ArtifactId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}

/// <summary>A bounded workflow-execution history page and its stable navigation anchors.</summary>
public sealed record WorkflowExecutionStatePage(
    IReadOnlyList<WorkflowExecutionState> Items,
    WorkflowExecutionStatePageCursor? PreviousCursor,
    WorkflowExecutionStatePageCursor? NextCursor,
    bool HasPrevious,
    bool HasNext,
    long TotalCount);

/// <summary>Shared workflow-execution history ordering and filter semantics for store providers.</summary>
public static class WorkflowExecutionStateHistory
{
    /// <summary>Effective timestamp used by the public run-history contract.</summary>
    public static DateTimeOffset SortTimestamp(WorkflowExecutionState state) =>
        state.UpdatedAt ?? state.CompletedAt ?? state.StartedAt ?? state.CreatedAt;

    /// <summary>Compares states in history order: effective timestamp descending, execution ID ascending.</summary>
    public static int Compare(WorkflowExecutionState left, WorkflowExecutionState right)
    {
        var timestamp = SortTimestamp(right).CompareTo(SortTimestamp(left));
        return timestamp != 0 ? timestamp : StringComparer.Ordinal.Compare(left.WorkflowExecutionId, right.WorkflowExecutionId);
    }

    /// <summary>Compares a state with a keyset cursor in history order.</summary>
    public static int Compare(WorkflowExecutionState state, WorkflowExecutionStatePageCursor cursor)
    {
        var timestamp = cursor.SortTimestamp.CompareTo(SortTimestamp(state));
        return timestamp != 0 ? timestamp : StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
    }

    /// <summary>Applies the provider-neutral filter semantics.</summary>
    public static bool Matches(WorkflowExecutionState state, WorkflowExecutionStatePageQuery query)
    {
        if (query.TenantId is { } tenantId && !StringComparer.Ordinal.Equals(state.TenantId, tenantId))
            return false;
        if (query.DefinitionId is { } definitionId && !StringComparer.Ordinal.Equals(state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId, definitionId))
            return false;
        if (query.Status is { } status && state.Status != status)
            return false;
        if (query.RunKind is { } runKind && state.RunKind != runKind)
            return false;
        if (query.CorrelationId is { } correlationId && !StringComparer.Ordinal.Equals(state.CorrelationId, correlationId))
            return false;
        if (query.WorkflowExecutionId is { } workflowExecutionId && !StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId))
            return false;
        if (query.ArtifactId is { } artifactId && !StringComparer.Ordinal.Equals(state.PinnedExecutable.ArtifactId, artifactId))
            return false;

        var timestamp = SortTimestamp(state);
        return (query.From is not { } from || timestamp >= from) && (query.To is not { } to || timestamp <= to);
    }

    /// <summary>Builds a cursor anchored to a page item.</summary>
    public static WorkflowExecutionStatePageCursor Cursor(
        WorkflowExecutionState state,
        WorkflowExecutionStatePageDirection direction,
        WorkflowExecutionStatePageQuery query) =>
        new(SortTimestamp(state), state.WorkflowExecutionId, direction, query.PageSize, query.Scope());

    /// <summary>
    /// Preserves the v1 HTTP contract: forward pages use the current request size, while backward pages cannot
    /// reach farther than the size embedded in their anchor and may still be narrowed by the current request.
    /// </summary>
    public static int EffectivePageSize(WorkflowExecutionStatePageQuery query) =>
        query.Cursor?.Direction == WorkflowExecutionStatePageDirection.Previous
            ? Math.Min(query.PageSize, query.Cursor.PageSize)
            : query.PageSize;
}
