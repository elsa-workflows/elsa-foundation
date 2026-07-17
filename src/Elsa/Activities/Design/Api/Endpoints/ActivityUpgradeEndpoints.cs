using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.UpgradePlans;

internal sealed class Create(ICommandSender sender, ILogger<Create> logger)
    : ActivityAuthoringCommandEndpoint<CreateActivityUpgradePlan, ActivityUpgradePlanView>(sender, logger)
{
    protected override int SuccessStatusCode => 201;
    protected override string GetLocation(ActivityUpgradePlanView response) =>
        $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}";

    public override void Configure()
    {
        Post(RouteConstants.GetRoute("upgrade-plans"));
        ConfigurePermissions(PermissionNames.ActivityDesignManage);
    }
}

internal sealed class Get(IRequestSender sender, ILogger<Get> logger)
    : ActivityAuthoringRequestEndpoint<GetActivityUpgradePlan, ActivityUpgradePlanView>(sender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("upgrade-plans/{planId}"));
        ConfigurePermissions(PermissionNames.ActivityDesignRead);
    }
}

internal sealed class Apply(ICommandSender sender, ILogger<Apply> logger)
    : ActivityAuthoringCommandEndpoint<ApplyActivityUpgradePlan, ActivityUpgradeApplyResultView>(sender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("upgrade-plans/{planId}/apply"));
        ConfigurePermissions(PermissionNames.ActivityDesignManage);
    }
}
