using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Versions.Get;

[Get("/design/activities/versions/{versionId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<GetReusableActivityVersion, ReusableActivityVersionView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ReusableActivityVersionView> HandleAsync(GetReusableActivityVersion request, CancellationToken cancellationToken) =>
        service.GetVersionAsync(request, cancellationToken);
}
