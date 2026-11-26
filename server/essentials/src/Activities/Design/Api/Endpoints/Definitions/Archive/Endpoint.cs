using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Archive;

[Delete("/design/activities/definitions/{definitionId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<ArchiveReusableActivityDefinition>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsArchive";
        options.BodyMode = EndpointBodyMode.None;
    }

    public override Task HandleAsync(ArchiveReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.ArchiveDefinitionAsync(command, cancellationToken);
}