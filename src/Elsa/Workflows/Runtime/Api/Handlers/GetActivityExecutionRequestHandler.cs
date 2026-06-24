using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetActivityExecutionRequestHandler(IActivityExecutionInspectionStore inspectionStore)
    : IRequestHandler<GetActivityExecution, GetActivityExecutionResponse>
{
    public async Task<GetActivityExecutionResponse> Handle(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);

        var projection = await inspectionStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        return new GetActivityExecutionResponse(projection is null ? null : ActivityExecutionInspectionView.From(projection));
    }
}
