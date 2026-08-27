using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Refresh;

[Post("/design/activities/upgrade-plans/{planId}/refresh")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityUpgradeOperations service) : ApiEndpoint<RefreshActivityUpgradePlan, ActivityUpgradePlanView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansRefresh";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<ActivityUpgradePlanView> HandleAsync(RefreshActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        var response = await service.RefreshPlanAsync(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}";
        return response;
    }
}
