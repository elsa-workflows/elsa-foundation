using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetActivityExecutionRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore inspectionStore,
    IActivityExecutionInspectionAuthorizationContextAsync authorization,
    IActivityExecutionHierarchyStore? hierarchyStore = null)
    : IRequestHandler<GetActivityExecution, GetActivityExecutionResponse>
{
    public async Task<GetActivityExecutionResponse> Handle(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);

        var workflowExecution = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null || !await authorization.CanInspectStructureAsync(workflowExecution, cancellationToken))
            return new GetActivityExecutionResponse(null);

        var projection = await inspectionStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        if (projection is null)
            return new GetActivityExecutionResponse(null);
        var boundary = hierarchyStore is null
            ? null
            : await hierarchyStore.FindBoundaryAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        var attempt = hierarchyStore is null
            ? null
            : await hierarchyStore.FindAttemptNavigationAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        return new GetActivityExecutionResponse(ActivityExecutionInspectionView.From(
            projection,
            boundary,
            await authorization.CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken),
            attempt,
            await authorization.CanResolveSensitiveValuePayloadsAsync(workflowExecution, cancellationToken)));
    }
}
