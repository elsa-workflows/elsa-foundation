using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

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
    string? Cursor = null)
{
    /// <summary>Validates the bounded request. The active store validates its opaque continuation.</summary>
    public void Validate()
    {
        if (PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "Page size must be greater than zero.");
        if (From is { } from && To is { } to && from > to)
            throw new ArgumentException("The workflow execution history start must not be after its end.", nameof(From));
        if (Cursor is not null && string.IsNullOrWhiteSpace(Cursor))
            throw new ArgumentException("The workflow execution history cursor cannot be blank.", nameof(Cursor));
    }
}

/// <summary>A bounded workflow-execution history page and its opaque forward continuation.</summary>
public sealed record WorkflowExecutionStatePage(
    IReadOnlyList<WorkflowExecutionState> Items,
    string? NextCursor,
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

    /// <summary>Returns a deterministic fingerprint over every filter that defines a result set.</summary>
    public static string Scope(WorkflowExecutionStatePageQuery query)
    {
        var json = JsonSerializer.Serialize(new object?[]
        {
            query.TenantId,
            query.DefinitionId,
            query.Status is { } status ? (int)status : null,
            query.RunKind is { } runKind ? (int)runKind : null,
            query.From?.UtcTicks,
            query.To?.UtcTicks,
            query.CorrelationId,
            query.WorkflowExecutionId,
            query.ArtifactId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
