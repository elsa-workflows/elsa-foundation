using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetActivityExecutionRequestHandler(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore inspectionStore)
    : IRequestHandler<GetActivityExecution, GetActivityExecutionResponse>
{
    public async Task<GetActivityExecutionResponse> Handle(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);

        var workflowExecution = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null)
            return new GetActivityExecutionResponse(null);

        var projection = await inspectionStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        return new GetActivityExecutionResponse(projection is null ? null : ActivityExecutionInspectionView.From(projection));
    }
}
