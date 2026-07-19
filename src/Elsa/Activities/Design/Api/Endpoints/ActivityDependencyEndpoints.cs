using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Endpoints;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Versions;

internal sealed class Dependencies(IRequestSender sender, ILogger<Dependencies> logger)
    : ActivityAuthoringRequestEndpoint<GetActivityDependencies, ActivityDependencyPageView>(sender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("versions/{versionId}/dependencies"));
        ConfigurePermissions(PermissionNames.ActivityDesignRead);
    }
}
