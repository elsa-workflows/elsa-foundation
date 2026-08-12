using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Attention;

/// <summary>
/// Shared record construction and urgency ordering for <see cref="IWorkflowRuntimeAttentionQuery"/>
/// implementations. Every store adapter traverses its own data differently, but the mapping of an
/// incident or faulted execution into an attention record — and what counts as "more urgent" — must be
/// identical, or the same runtime state would rank differently per provider.
/// </summary>
public static class WorkflowRuntimeAttentionRecords
{
    /// <summary>
    /// Blocking incidents first, then faulted executions, then open incidents; newest observation first
    /// within a kind; execution id and incident id as deterministic tiebreaks.
    /// </summary>
    public static readonly IComparer<WorkflowRuntimeAttentionRecord> UrgencyComparer =
        Comparer<WorkflowRuntimeAttentionRecord>.Create(Compare);

    public static WorkflowRuntimeAttentionRecord MapIncident(
        IncidentState incident,
        WorkflowExecutionState execution,
        DateTimeOffset observedAt) => new(
        execution.WorkflowExecutionId,
        execution.PinnedExecutable.DefinitionId,
        incident.IncidentId,
        incident.Status == IncidentStatus.Blocking
            ? WorkflowRuntimeAttentionKind.BlockingIncident
            : WorkflowRuntimeAttentionKind.OpenIncident,
        $"{incident.IncidentId}:{incident.CreatedAt.UtcTicks}:{incident.Status}:{incident.Severity}",
        incident.CreatedAt,
        Later(observedAt, incident.CreatedAt),
        1,
        null);

    public static WorkflowRuntimeAttentionRecord MapFault(
        WorkflowExecutionState execution,
        DateTimeOffset observedAt)
    {
        var occurredAt = execution.CompletedAt ?? execution.UpdatedAt ?? execution.StartedAt ?? execution.CreatedAt;
        return new(
            execution.WorkflowExecutionId,
            execution.PinnedExecutable.DefinitionId,
            null,
            WorkflowRuntimeAttentionKind.FaultedExecution,
            $"{execution.WorkflowExecutionId}:{occurredAt.UtcTicks}:{execution.Status}",
            occurredAt,
            Later(observedAt, occurredAt),
            1,
            null);
    }

    private static int Compare(WorkflowRuntimeAttentionRecord left, WorkflowRuntimeAttentionRecord right)
    {
        var kind = KindOrder(left.Kind).CompareTo(KindOrder(right.Kind));
        if (kind != 0)
            return kind;

        var observed = right.LastObservedAt.CompareTo(left.LastObservedAt);
        if (observed != 0)
            return observed;

        var executionId = StringComparer.Ordinal.Compare(left.WorkflowExecutionId, right.WorkflowExecutionId);
        return executionId != 0
            ? executionId
            : StringComparer.Ordinal.Compare(left.IncidentId ?? string.Empty, right.IncidentId ?? string.Empty);
    }

    private static int KindOrder(WorkflowRuntimeAttentionKind kind) => kind switch
    {
        WorkflowRuntimeAttentionKind.BlockingIncident => 0,
        WorkflowRuntimeAttentionKind.FaultedExecution => 1,
        _ => 2
    };

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;
}
