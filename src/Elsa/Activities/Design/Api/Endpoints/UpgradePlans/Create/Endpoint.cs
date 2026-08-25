using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Create;

[Post("/design/activities/upgrade-plans")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityUpgradeOperations service) : ApiEndpoint<CreateActivityUpgradePlan, ActivityUpgradePlanView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansCreate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
    }

    public override async Task<ActivityUpgradePlanView> HandleAsync(CreateActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        var response = await service.CreatePlanAsync(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}";
        return response;
    }
}
