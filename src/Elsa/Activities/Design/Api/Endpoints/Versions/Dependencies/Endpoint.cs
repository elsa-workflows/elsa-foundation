using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Versions.Dependencies;

[Get("/design/activities/versions/{versionId}/dependencies")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<GetActivityDependencies, ActivityDependencyPageView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsDependencies";
        options.Accepts = ["*/*", "application/json"];
        options.StrictTypedParsing = true;
    }

    public override Task<ActivityDependencyPageView> HandleAsync(GetActivityDependencies request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}
