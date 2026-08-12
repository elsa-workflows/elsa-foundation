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
                    Consider(top, WorkflowRuntimeAttentionRecords.MapIncident(incident, execution, observedAt), request.MaximumItems);
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
            foreach (var execution in page.Items.Where(x => !activeExecutionIds.Contains(x.WorkflowExecutionId)))
            {
                totalCount = checked(totalCount + 1);
                Consider(top, WorkflowRuntimeAttentionRecords.MapFault(execution, observedAt), request.MaximumItems);
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
        top.Sort(WorkflowRuntimeAttentionRecords.UrgencyComparer);
        if (top.Count > maximumItems)
            top.RemoveAt(top.Count - 1);
    }
}
