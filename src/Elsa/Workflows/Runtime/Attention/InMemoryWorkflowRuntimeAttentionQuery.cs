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

        var incidentRecords = activeIncidents.Select(incident => MapIncident(
            incident,
            tenantExecutions[incident.WorkflowExecutionId],
            observedAt));
        var faultRecords = tenantExecutions.Values
            .Where(execution => execution.Status == WorkflowExecutionStatus.Faulted)
            .Where(execution => !executionsWithIncidents.Contains(execution.WorkflowExecutionId))
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
            .ToArray();

        return new(all.Length, all.Take(request.MaximumItems).ToArray());
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
