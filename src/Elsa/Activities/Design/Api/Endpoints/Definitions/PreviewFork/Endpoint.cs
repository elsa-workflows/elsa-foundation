using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.PreviewFork;

[Post("/design/activities/definitions/{definitionId}/fork-previews")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IActivityForkService service) : ApiEndpoint<PreviewReusableActivityFork, ActivityForkPreviewView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsPreviewFork";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityForkPreviewView> HandleAsync(PreviewReusableActivityFork command, CancellationToken cancellationToken) =>
        service.PreviewAsync(command, cancellationToken);
}
