using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Restore;

[Post("/design/activities/definitions/{definitionId}/restore")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class RestoreEndpoint(IReusableActivityAuthoringService service) : ApiEndpoint<RestoreReusableActivityDefinition>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsRestore";
        options.BodyMode = EndpointBodyMode.None;
    }

    public override Task HandleAsync(RestoreReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.RestoreDefinitionAsync(command, cancellationToken);
}