using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Get;

[Get("/design/activities/definitions/{definitionId}")]
[RequirePermission(ActivityDesignPermissions.Read)]
[AuthoringProblems]
public sealed class Endpoint(IActivityDefinitionManagementProjectionService service) : ApiEndpoint<GetReusableActivityDefinition, ReusableActivityDefinitionManagementView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ReusableActivityDefinitionManagementView> HandleAsync(GetReusableActivityDefinition request, CancellationToken cancellationToken) =>
        service.GetDefinitionAsync(request, cancellationToken);
}
