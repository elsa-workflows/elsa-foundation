using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Layout;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution layout")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityExecutionLayout, ActivityExecutionLayoutView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityLayout";

    public override async Task<ActivityExecutionLayoutView> HandleAsync(GetActivityExecutionLayout request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(request, cancellationToken);
        return response.Layout ?? throw new ActivityExecutionMissingSignal();
    }
}
