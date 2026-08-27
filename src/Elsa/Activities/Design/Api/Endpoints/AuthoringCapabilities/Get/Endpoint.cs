using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.AuthoringCapabilities.Get;

[Get("/design/activities/authoring-capabilities")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IActivityAuthoringCapabilitiesReader service) : ApiEndpointWithoutRequest<ActivityAuthoringCapabilitiesView>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "AuthoringCapabilitiesGet";

    public override Task<ActivityAuthoringCapabilitiesView> HandleAsync(CancellationToken cancellationToken) =>
        service.GetAsync(new GetActivityAuthoringCapabilities(), cancellationToken);
}
