using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Refresh;

[Post("/design/activities/upgrade-plans/{planId}/refresh")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<RefreshActivityUpgradePlan, ActivityUpgradePlanView>
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
        var response = await sender.Send(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}";
        return response;
    }
}
