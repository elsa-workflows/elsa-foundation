using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetWorkflowInstanceRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore activityExecutionInspectionStore,
    IIncidentStateStore incidentStateStore)
    : IRequestHandler<GetWorkflowInstance, GetWorkflowInstanceResponse>
{
    public async Task<GetWorkflowInstanceResponse> Handle(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);

        var state = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (state is null)
            return new GetWorkflowInstanceResponse(null);

        var activities = (await activityExecutionInspectionStore.ListSummariesAsync(request.WorkflowExecutionId, cancellationToken))
            .OrderBy(activity => activity.ExecutionSequence)
            .ThenBy(activity => activity.ScheduledAt)
            .ThenBy(activity => activity.ActivityExecutionId, StringComparer.Ordinal)
            .Select(ActivityExecutionInspectionSummaryView.From)
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
