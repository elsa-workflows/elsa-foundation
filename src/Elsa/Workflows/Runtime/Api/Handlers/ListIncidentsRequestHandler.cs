using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Operator-facing incident query (RT-5). Surfaces the incidents recorded for a workflow execution so that a workflow
/// that has transitioned to <see cref="Core.Models.WorkflowExecutionStatus.Faulted"/> because of a blocking incident is
/// observable and diagnosable, optionally filtered to blocking incidents only.
/// </summary>
public sealed class ListIncidentsRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IIncidentStateStore incidentStateStore,
    IActivityExecutionInspectionAuthorizationContext authorization)
    : IRequestHandler<ListIncidents, ListIncidentsResponse>
{
    public async Task<ListIncidentsResponse> Handle(ListIncidents request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);

        var workflowExecution = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null || !authorization.CanInspectStructure(workflowExecution))
            return new ListIncidentsResponse(false, [], 0);

        var canInspectSensitiveValues = authorization.CanInspectSensitiveValues(workflowExecution);

        var incidents = request.BlockingOnly
            ? await incidentStateStore.ListBlockingAsync(request.WorkflowExecutionId, cancellationToken)
            : await incidentStateStore.ListAsync(request.WorkflowExecutionId, cancellationToken);

        var views = incidents
            .OrderByDescending(incident => incident.CreatedAt)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Select(incident => IncidentStateView.From(incident, canInspectSensitiveValues))
            .ToArray();

        return new ListIncidentsResponse(true, views, views.Length);
    }
}
