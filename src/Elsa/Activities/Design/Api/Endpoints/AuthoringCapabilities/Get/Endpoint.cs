using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.AuthoringCapabilities.Get;

[Get("/design/activities/authoring-capabilities")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IRequestSender sender) : ApiEndpointWithoutRequest<ActivityAuthoringCapabilitiesView>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "AuthoringCapabilitiesGet";

    public override Task<ActivityAuthoringCapabilitiesView> HandleAsync(CancellationToken cancellationToken) =>
        sender.Send(new GetActivityAuthoringCapabilities(), cancellationToken);
}
