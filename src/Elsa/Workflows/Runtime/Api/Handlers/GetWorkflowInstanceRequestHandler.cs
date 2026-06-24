using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetWorkflowInstanceRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore)
    : IRequestHandler<GetWorkflowInstance, GetWorkflowInstanceResponse>
{
    public async Task<GetWorkflowInstanceResponse> Handle(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);

        var state = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (state is null)
            return new GetWorkflowInstanceResponse(null);

        var activities = (await activityExecutionStateStore.ListAsync(request.WorkflowExecutionId, cancellationToken))
            .OrderBy(activity => activity.ScheduledAt)
            .ThenBy(activity => activity.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .Select(ActivityExecutionStateView.From)
            .ToArray();
        var incidents = (await incidentStateStore.ListAsync(request.WorkflowExecutionId, cancellationToken))
            .OrderBy(incident => incident.CreatedAt)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Select(IncidentStateView.From)
            .ToArray();

        return new GetWorkflowInstanceResponse(new WorkflowInstanceDetailsView(
            WorkflowInstanceSummaryView.From(state, activities.Length, incidents.Length),
            activities,
            incidents));
    }
}
