using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Endpoints.AlterationPlans.Cancel;

[Post(AlterationRouteConstants.Cancel)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)]
[AlterationProblems("cancelling runtime alteration plan", "InvalidAlterationPlanId", "The alteration plan identifier is invalid.")]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithResult<CancelWorkflowAlterationPlan, WorkflowAlterationPlanView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "CancelAlteration";
        // A bodyless POST: the plan id binds from the route, and the operation declares no accepts
        // metadata, exactly as the hand-written mapper published it.
        options.BodyMode = EndpointBodyMode.None;
    }

    public override async Task<EndpointResult<WorkflowAlterationPlanView>> HandleAsync(CancelWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return EndpointResult.Status(
            result.IsTerminalNoOp ? StatusCodes.Status200OK : StatusCodes.Status202Accepted,
            result.Plan);
    }
}
