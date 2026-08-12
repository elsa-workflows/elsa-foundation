using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Attention;

/// <summary>
/// Exact adapter for the default volatile runtime stores. It takes one execution snapshot and one incident
/// snapshot, joins them in-process, and returns only the most urgent requested records. It never performs a
/// per-execution incident query and never treats a bounded page as the complete dataset.
/// </summary>
public sealed class InMemoryWorkflowRuntimeAttentionQuery(
    InMemoryWorkflowExecutionStateStore executions,
    InMemoryIncidentStateStore incidents,
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

        var executionSnapshot = await executions.ListAsync(cancellationToken);
        var incidentSnapshot = await incidents.ListAllAsync(cancellationToken);
        var tenantExecutions = executionSnapshot
            .Where(execution => string.Equals(execution.TenantId, request.TenantId, StringComparison.Ordinal))
            .ToDictionary(execution => execution.WorkflowExecutionId, StringComparer.Ordinal);
        var activeIncidents = incidentSnapshot
            .Where(incident => tenantExecutions.ContainsKey(incident.WorkflowExecutionId))
            .Where(incident => incident.Status is IncidentStatus.Open or IncidentStatus.Blocking)
            .ToArray();
        var executionsWithIncidents = activeIncidents
            .Select(incident => incident.WorkflowExecutionId)
            .ToHashSet(StringComparer.Ordinal);
        var observedAt = _timeProvider.GetUtcNow();

        var incidentRecords = activeIncidents.Select(incident => WorkflowRuntimeAttentionRecords.MapIncident(
            incident,
            tenantExecutions[incident.WorkflowExecutionId],
            observedAt));
        var faultRecords = tenantExecutions.Values
            .Where(execution => execution.Status == WorkflowExecutionStatus.Faulted)
            .Where(execution => !executionsWithIncidents.Contains(execution.WorkflowExecutionId))
            .Select(execution => WorkflowRuntimeAttentionRecords.MapFault(execution, observedAt));
        var all = incidentRecords
            .Concat(faultRecords)
            .Order(WorkflowRuntimeAttentionRecords.UrgencyComparer)
            .ToArray();

        return new(all.Length, all.Take(request.MaximumItems).ToArray());
    }
}
