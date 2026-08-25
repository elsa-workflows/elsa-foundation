using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Endpoints.ActivityExecutions.ValuePayload;

[Get("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("resolving activity execution value evidence")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithResult<GetActivityExecutionValuePayload, ActivityExecutionValuePayloadView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetActivityValuePayload";

    public override async Task<EndpointResult<ActivityExecutionValuePayloadView>> HandleAsync(GetActivityExecutionValuePayload request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(request, cancellationToken);
        var result = response.Result;
        if (result.Value is not null)
        {
            var status = result.Outcome switch
            {
                ActivityExecutionValuePayloadReadOutcome.Resolved => StatusCodes.Status200OK,
                ActivityExecutionValuePayloadReadOutcome.Denied => StatusCodes.Status403Forbidden,
                ActivityExecutionValuePayloadReadOutcome.Unavailable => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status404NotFound
            };
            return EndpointResult.Status(status, result.Value);
        }

        throw result.Outcome switch
        {
            ActivityExecutionValuePayloadReadOutcome.Denied => new ActivityValuePayloadDeniedSignal(),
            ActivityExecutionValuePayloadReadOutcome.Unavailable => new ActivityValuePayloadUnavailableSignal(),
            _ => (Exception)new ActivityExecutionMissingSignal()
        };
    }
}
