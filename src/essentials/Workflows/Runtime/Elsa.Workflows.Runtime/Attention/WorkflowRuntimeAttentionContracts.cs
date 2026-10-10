using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Attention;

[RuntimeOperationalStateContract]
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

/// <summary>
/// Reports work that waits because no active member can run it (spec 184, FR-017). The distributed runtime implements
/// it; <see cref="WorkflowRuntimeAttentionContributor"/> turns each report into a warning beside the runtime's other
/// attention items, so the core gains no reference to that leaf.
/// </summary>
/// <remarks>
/// A report names what the waiting executions need and how many there are, and never a host: no domain answer reveals
/// the fleet's topology (spec 182, FR-011). Refusing such work is placement's decision, not a runtime fault, so it is
/// never reported as an incident or a faulted run.
/// </remarks>
public interface IWorkflowRuntimePlacementAttention
{
    /// <summary>The current persistence scope's unplaceable work, one report per unmet requirement.</summary>
    ValueTask<IReadOnlyCollection<UnplaceableWorkReport>> ListUnplaceableWorkAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Executions waiting in the durable transport for a member that can run them: <see cref="Requirement"/> describes what
/// no active member satisfies, or why the requirement could not be resolved.
/// </summary>
public sealed record UnplaceableWorkReport(
    string Requirement,
    int WaitingExecutions,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt);

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
