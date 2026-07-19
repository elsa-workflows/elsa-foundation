using Elsa.Attention.Core;

namespace Elsa.Workflows.Runtime.Attention;

public interface IWorkflowRuntimeAttentionQuery
{
    /// <summary>
    /// Evaluates the complete authorized runtime dataset and returns only the most urgent bounded records.
    /// Implementations must never infer all-clear from a page or sample.
    /// </summary>
    ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(
        WorkflowRuntimeAttentionQuery request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowRuntimeAttentionQuery(AttentionQueryContext Context, int MaximumItems)
{
    public string? TenantId => Context.TenantId;
}

public enum WorkflowRuntimeAttentionKind
{
    FaultedExecution,
    OpenIncident,
    BlockingIncident
}

public sealed record WorkflowRuntimeAttentionRecord(
    string WorkflowExecutionId,
    string WorkflowDefinitionId,
    string? IncidentId,
    WorkflowRuntimeAttentionKind Kind,
    string Generation,
    DateTimeOffset OccurredAt,
    DateTimeOffset LastObservedAt,
    int Count,
    string? SanitizedSummary);

public sealed record WorkflowRuntimeAttentionSnapshot(
    int TotalCount,
    IReadOnlyCollection<WorkflowRuntimeAttentionRecord> Records,
    string? ErrorCode = null,
    string? Detail = null)
{
    public bool IsAvailable => string.IsNullOrWhiteSpace(ErrorCode);

    public static WorkflowRuntimeAttentionSnapshot Unavailable(string errorCode, string? detail = null) =>
        new(0, [], errorCode, detail);
}

public sealed class UnavailableWorkflowRuntimeAttentionQuery : IWorkflowRuntimeAttentionQuery
{
    public ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(
        WorkflowRuntimeAttentionQuery request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(WorkflowRuntimeAttentionSnapshot.Unavailable(
            "RUNTIME_ATTENTION_QUERY_UNAVAILABLE",
            "The active workflow runtime persistence provider does not supply a complete attention query adapter."));
}
