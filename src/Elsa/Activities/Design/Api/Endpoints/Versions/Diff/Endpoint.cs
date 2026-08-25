using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Versions.Diff;

[Get("/design/activities/versions/{fromVersionId}/diff/{toVersionId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IActivityVersionDiffService service) : ApiEndpoint<CompareActivityVersions, ActivityVersionDiffView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsDiff";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ActivityVersionDiffView> HandleAsync(CompareActivityVersions request, CancellationToken cancellationToken) =>
        service.CompareVersionsAsync(request, cancellationToken);
}
