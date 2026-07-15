using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Retire(ICommandSender sender, ILogger<Retire> logger)
    : ActivityAuthoringCommandEndpoint<RetireReusableActivityVersion, ReusableActivityVersionLifecycleView>(sender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/retire"));
        ConfigurePermissions();
    }
}

internal sealed class Restore(ICommandSender sender, ILogger<Restore> logger)
    : ActivityAuthoringCommandEndpoint<RestoreReusableActivityVersion, ReusableActivityVersionLifecycleView>(sender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/restore"));
        ConfigurePermissions();
    }
}

internal sealed class Revoke(ICommandSender sender, ILogger<Revoke> logger)
    : ActivityAuthoringCommandEndpoint<RevokeReusableActivityVersion, ReusableActivityVersionLifecycleView>(sender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/{versionId}/revoke"));
        ConfigurePermissions();
    }
}
