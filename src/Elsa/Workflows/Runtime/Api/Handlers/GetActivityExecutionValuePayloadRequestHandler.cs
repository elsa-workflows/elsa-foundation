using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetActivityExecutionValuePayloadRequestHandler(IActivityExecutionValuePayloadReader reader)
    : IRequestHandler<GetActivityExecutionValuePayload, GetActivityExecutionValuePayloadResponse>
{
    public async Task<GetActivityExecutionValuePayloadResponse> Handle(
        GetActivityExecutionValuePayload request,
        CancellationToken cancellationToken) =>
        new(await reader.ReadAsync(
            request.WorkflowExecutionId,
            request.ActivityExecutionId,
            request.EvidenceId,
            cancellationToken));
}
