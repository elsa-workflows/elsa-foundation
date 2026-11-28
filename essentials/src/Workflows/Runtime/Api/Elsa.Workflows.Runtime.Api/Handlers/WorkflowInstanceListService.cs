using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class WorkflowInstanceListService(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore,
    IActivityInspectionContextAsync authorization) : IWorkflowInstanceListService
{
    public Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
        new WorkflowInstanceListProjection(authorization, workflowExecutionStateStore.QueryPageAsync,
            activityExecutionStateStore, incidentStateStore, FindNativeHealth).ListAsync(request, cancellationToken);

    private IWorkflowHealthQuery? FindNativeHealth() =>
        workflowExecutionStateStore is IWorkflowHealthQuery health && health.SupportsIncidentStore(incidentStateStore)
            ? health
            : null;
}
