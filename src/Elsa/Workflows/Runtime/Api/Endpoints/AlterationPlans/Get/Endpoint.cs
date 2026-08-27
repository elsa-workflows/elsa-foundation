using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.AlterationPlans.Get;

[Get(AlterationRouteConstants.Plan)]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[AlterationProblems("handling runtime alteration request", "InvalidAlterationPlanId", "The alteration plan identifier is invalid.", EntityNotFoundArm = true)]
public sealed class Endpoint(IWorkflowAlterationPlanApiService alterations) : ApiEndpoint<GetWorkflowAlterationPlan, WorkflowAlterationPlanView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetAlteration";

    public override Task<WorkflowAlterationPlanView> HandleAsync(GetWorkflowAlterationPlan request, CancellationToken cancellationToken) =>
        alterations.GetPlanAsync(request, cancellationToken);
}
