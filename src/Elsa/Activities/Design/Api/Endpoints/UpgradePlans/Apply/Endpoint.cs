using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans.Apply;

[Post("/design/activities/upgrade-plans/{planId}/apply")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<ApplyActivityUpgradePlan, ActivityUpgradeApplyResultView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "UpgradePlansApply";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityUpgradeApplyResultView> HandleAsync(ApplyActivityUpgradePlan command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
