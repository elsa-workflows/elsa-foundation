using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Get;

[Get("/design/activities/upgrade-plans/{planId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityUpgradePlan, ActivityUpgradePlanView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ActivityUpgradePlanView> HandleAsync(GetActivityUpgradePlan request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
