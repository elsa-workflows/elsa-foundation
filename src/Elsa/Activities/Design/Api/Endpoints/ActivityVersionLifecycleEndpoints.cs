using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Retire(ICommandSender sender, ILogger<Retire> logger)
    : ActivityAuthoringRouteCommandEndpoint<RetireReusableActivityVersion, ReusableActivityVersionLifecycleView>(
        sender,
        logger,
        "versionId",
        static (request, versionId) => request with { VersionId = versionId })
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/retire"));
        ConfigurePermissions(PermissionNames.ActivityDesignManage);
    }
}

internal sealed class Restore(ICommandSender sender, ILogger<Restore> logger)
    : ActivityAuthoringRouteCommandEndpoint<RestoreReusableActivityVersion, ReusableActivityVersionLifecycleView>(
        sender,
        logger,
        "versionId",
        static (request, versionId) => request with { VersionId = versionId })
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/restore"));
        ConfigurePermissions(PermissionNames.ActivityDesignManage);
    }
}

internal sealed class Revoke(ICommandSender sender, ILogger<Revoke> logger)
    : ActivityAuthoringRouteCommandEndpoint<RevokeReusableActivityVersion, ReusableActivityVersionLifecycleView>(
        sender,
        logger,
        "versionId",
        static (request, versionId) => request with { VersionId = versionId })
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/revoke"));
        ConfigurePermissions(PermissionNames.ActivityDesignManage);
    }
}
