using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Descendants;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution descendants")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityExecutionDescendants, ActivityExecutionHierarchyPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityDescendants";

    public override async Task<ActivityExecutionHierarchyPageView> HandleAsync(GetActivityExecutionDescendants request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(request, cancellationToken);
        return response.Page ?? throw new ActivityExecutionMissingSignal();
    }
}
