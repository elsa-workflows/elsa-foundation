using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class GetActivityExecutionDescendantsRequestHandler(ActivityExecutionHierarchyReader reader)
    : IRequestHandler<GetActivityExecutionDescendants, GetActivityExecutionDescendantsResponse>
{
    public async Task<GetActivityExecutionDescendantsResponse> Handle(GetActivityExecutionDescendants request, CancellationToken cancellationToken) =>
        new(await reader.ReadAsync(request.WorkflowExecutionId, request.ActivityExecutionId, request.Cursor, request.Limit, request.Include, cancellationToken));
}

public sealed class GetActivityExecutionLayoutRequestHandler(ActivityExecutionLayoutReader reader)
    : IRequestHandler<GetActivityExecutionLayout, GetActivityExecutionLayoutResponse>
{
    public async Task<GetActivityExecutionLayoutResponse> Handle(GetActivityExecutionLayout request, CancellationToken cancellationToken) =>
        new(await reader.ReadAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken));
}
