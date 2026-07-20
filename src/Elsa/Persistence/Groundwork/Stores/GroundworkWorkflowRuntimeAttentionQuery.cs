using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Complete, tenant-bound workflow runtime attention query over the Groundwork runtime stores.
/// Both data sets are traversed through declared finite pages; healthy execution history is never
/// read, and the returned selection remains bounded by the requested item count.
/// </summary>
public sealed class GroundworkWorkflowRuntimeAttentionQuery(
    GroundworkWorkflowExecutionStateStore executions,
    GroundworkIncidentStateStore incidents,
    TimeProvider? timeProvider = null) : IWorkflowRuntimeAttentionQuery
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowRuntimeAttentionSnapshot> QueryAsync(
        WorkflowRuntimeAttentionQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.MaximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum items must be positive.");
        if (string.IsNullOrWhiteSpace(request.TenantId))
            return WorkflowRuntimeAttentionSnapshot.Unavailable(
                "RUNTIME_ATTENTION_TENANT_REQUIRED",
                "Workflow runtime attention requires an authenticated tenant scope.");

        var observedAt = _timeProvider.GetUtcNow();
        var top = new List<WorkflowRuntimeAttentionRecord>(request.MaximumItems);
        var activeExecutionIds = new HashSet<string>(StringComparer.Ordinal);
        var executionCache = new Dictionary<string, WorkflowExecutionState?>(StringComparer.Ordinal);
        var totalCount = 0;

        foreach (var status in new[] { IncidentStatus.Open, IncidentStatus.Blocking })
        {
            string? continuation = null;
            do
            {
                var page = await incidents.QueryAttentionPageAsync(
                    status,
                    continuation: continuation,
                    cancellationToken: cancellationToken);
                foreach (var incident in page.Items)
                {
                    if (!executionCache.TryGetValue(incident.WorkflowExecutionId, out var execution))
                    {
                        execution = await executions.FindAsync(incident.WorkflowExecutionId, cancellationToken);
                        executionCache.Add(incident.WorkflowExecutionId, execution);
                    }
                    if (execution is null || !StringComparer.Ordinal.Equals(execution.TenantId, request.TenantId))
                        continue;

                    activeExecutionIds.Add(incident.WorkflowExecutionId);
                    totalCount = checked(totalCount + 1);
                    Consider(top, MapIncident(incident, execution, observedAt), request.MaximumItems);
                }

                continuation = page.NextContinuation;
            } while (continuation is not null);
        }

        string? cursor = null;
        do
        {
            var page = await executions.QueryFaultedForAttentionAsync(
                request.TenantId,
                cursor,
                cancellationToken);
            foreach (var execution in page.Items)
            {
                if (activeExecutionIds.Contains(execution.WorkflowExecutionId))
                    continue;

                totalCount = checked(totalCount + 1);
                Consider(top, MapFault(execution, observedAt), request.MaximumItems);
            }

            cursor = page.NextCursor;
        } while (cursor is not null);

        return new WorkflowRuntimeAttentionSnapshot(totalCount, top);
    }

    private static void Consider(
        List<WorkflowRuntimeAttentionRecord> top,
        WorkflowRuntimeAttentionRecord candidate,
        int maximumItems)
    {
        top.Add(candidate);
        top.Sort(CompareAttention);
        if (top.Count > maximumItems)
            top.RemoveAt(top.Count - 1);
    }

    private static int CompareAttention(WorkflowRuntimeAttentionRecord left, WorkflowRuntimeAttentionRecord right)
    {
        var kind = AttentionKindOrder(left.Kind).CompareTo(AttentionKindOrder(right.Kind));
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

    private static int AttentionKindOrder(WorkflowRuntimeAttentionKind kind) => kind switch
    {
        WorkflowRuntimeAttentionKind.BlockingIncident => 0,
        WorkflowRuntimeAttentionKind.FaultedExecution => 1,
        _ => 2
    };

    private static WorkflowRuntimeAttentionRecord MapIncident(
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

    private static WorkflowRuntimeAttentionRecord MapFault(
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

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;
}
