using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetWorkflowInstanceRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore activityExecutionInspectionStore,
    IIncidentStateStore incidentStateStore,
    IDurableValueStateStore durableValueStateStore,
    IRuntimePayloadCapturePolicy payloadCapturePolicy,
    IActivityExecutionInspectionAuthorizationContext authorization)
    : IRequestHandler<GetWorkflowInstance, GetWorkflowInstanceResponse>
{
    public async Task<GetWorkflowInstanceResponse> Handle(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);

        var state = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (state is null || !authorization.CanInspectStructure(state))
            return new GetWorkflowInstanceResponse(null);

        var canInspectSensitiveValues = authorization.CanInspectSensitiveValues(state);

        var activityPage = await activityExecutionInspectionStore.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery(
                request.WorkflowExecutionId,
                request.ActivityPageSize,
                request.ActivityContinuationToken),
            cancellationToken);
        var activities = activityPage.Items
            .Select(ActivityExecutionInspectionSummaryView.From)
            .ToArray();
        var incidents = (await incidentStateStore.ListAsync(request.WorkflowExecutionId, cancellationToken))
            .OrderBy(incident => incident.CreatedAt)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Select(incident => IncidentStateView.From(incident, canInspectSensitiveValues))
            .ToArray();

        // Workflow outputs (#254 Seam R1): read-only projection of the instance's SetOutput-assigned durable
        // values, with every payload routed through the configured capture policy (declined payloads surface
        // as named redacted markers, never silently absent).
        var outputProjections = RuntimeWorkflowOutputStateProjection
            .Project(await durableValueStateStore.ListAllDurableValueStatesAsync(request.WorkflowExecutionId, cancellationToken), payloadCapturePolicy)
            .ToArray();
        var outputs = outputProjections.ToDictionary(
            output => output.Name,
            output => canInspectSensitiveValues
                ? WorkflowOutputView.From(output)
                : WorkflowOutputView.Redacted(output.CapturedAt),
            StringComparer.Ordinal);

        return new GetWorkflowInstanceResponse(new WorkflowInstanceDetailsView(
            WorkflowInstanceSummaryView.From(state, activityPage.TotalCount, incidents.Length, canInspectSensitiveValues),
            activities,
            incidents,
            outputs,
            activityPage.NextContinuationToken));
    }
}
