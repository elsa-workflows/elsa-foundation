using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.Get;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading activity execution")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityExecution, ActivityExecutionInspectionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityExecution";

    public override async Task<ActivityExecutionInspectionView> HandleAsync(GetActivityExecution request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(request, cancellationToken);
        return response.ActivityExecution ?? throw new ActivityExecutionMissingSignal();
    }
}
