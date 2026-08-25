using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Endpoints.AlterationPlans.Submit;

[Post(AlterationRouteConstants.Plans)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)]
[AlterationSubmitProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithResult<SubmitWorkflowAlterationPlan, WorkflowAlterationPlanSubmissionView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "SubmitAlteration";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
        options.SuccessStatus = StatusCodes.Status202Accepted;
        // The published OpenAPI documents this route as 200, as the hand-written mapper always did.
        options.DocumentedStatus = StatusCodes.Status200OK;
    }

    public override async Task<EndpointResult<WorkflowAlterationPlanSubmissionView>> HandleAsync(SubmitWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        request = request with { IdempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString() };
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new RuntimeAlterationRequestRejectedSignal("MissingIdempotencyKey", "The Idempotency-Key header is required.");
        if (request.IdempotencyKey.Length > SubmitWorkflowAlterationPlanRequestHandler.MaximumIdempotencyKeyLength)
            throw new RuntimeAlterationRequestRejectedSignal("InvalidIdempotencyKey", "The Idempotency-Key header must not exceed 256 characters.");

        var result = await sender.Send(request, cancellationToken);
        HttpContext.Response.Headers.Location = result.Links.Self;
        return EndpointResult.Status(StatusCodes.Status202Accepted, result);
    }
}
