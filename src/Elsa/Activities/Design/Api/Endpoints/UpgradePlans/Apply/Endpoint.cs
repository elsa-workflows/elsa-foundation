using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Apply;

[Post("/design/activities/upgrade-plans/{planId}/apply")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityUpgradeOperations service) : ApiEndpoint<ApplyActivityUpgradePlan, ActivityUpgradeApplyResultView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansApply";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityUpgradeApplyResultView> HandleAsync(ApplyActivityUpgradePlan command, CancellationToken cancellationToken) =>
        service.ApplyPlanAsync(command, cancellationToken);
}
