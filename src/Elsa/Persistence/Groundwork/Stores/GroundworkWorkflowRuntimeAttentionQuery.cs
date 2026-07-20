using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Complete, tenant-bound workflow runtime attention query over the Groundwork runtime stores.
/// Both data sets are traversed through declared finite pages; this adapter deliberately avoids
/// the compatibility <c>ListAllAsync</c> APIs and per-execution incident reads.
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

        var executionsById = await ListTenantExecutionsAsync(request.TenantId, cancellationToken);
        var activeIncidents = await incidents.ListActiveForAttentionAsync(cancellationToken);
        var observedAt = _timeProvider.GetUtcNow();
        var incidentRecords = activeIncidents
            .Where(incident => executionsById.TryGetValue(incident.WorkflowExecutionId, out _))
            .Select(incident => MapIncident(incident, executionsById[incident.WorkflowExecutionId], observedAt));
        var executionIdsWithIncidents = activeIncidents
            .Where(incident => executionsById.ContainsKey(incident.WorkflowExecutionId))
            .Select(incident => incident.WorkflowExecutionId)
            .ToHashSet(StringComparer.Ordinal);
        var faultRecords = executionsById.Values
            .Where(execution => execution.Status == WorkflowExecutionStatus.Faulted)
            .Where(execution => !executionIdsWithIncidents.Contains(execution.WorkflowExecutionId))
            .Select(execution => MapFault(execution, observedAt));
        var all = incidentRecords
            .Concat(faultRecords)
            .OrderBy(record => record.Kind switch
            {
                WorkflowRuntimeAttentionKind.BlockingIncident => 0,
                WorkflowRuntimeAttentionKind.FaultedExecution => 1,
                _ => 2
            })
            .ThenByDescending(record => record.LastObservedAt)
            .ThenBy(record => record.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(record => record.IncidentId, StringComparer.Ordinal)
            .ToArray();

        return new WorkflowRuntimeAttentionSnapshot(all.Length, all.Take(request.MaximumItems).ToArray());
    }

    private async ValueTask<IReadOnlyDictionary<string, WorkflowExecutionState>> ListTenantExecutionsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, WorkflowExecutionState>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await executions.QueryPageAsync(
                new WorkflowExecutionStatePageQuery(
                    ElsaGroundworkQueryRoutes.MaximumResultCount,
                    TenantId: tenantId,
                    Cursor: cursor),
                cancellationToken);
            foreach (var state in page.Items)
                states.Add(state.WorkflowExecutionId, state);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return states;
    }

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
