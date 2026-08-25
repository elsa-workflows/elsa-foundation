using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.Update;

[Patch("/design/activities/definitions/{definitionId}")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<UpdateReusableActivityDefinition, ActivityDefinitionIdentityView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsUpdate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityDefinitionIdentityView> HandleAsync(UpdateReusableActivityDefinition command, CancellationToken cancellationToken) =>
        service.UpdateDefinitionAsync(command, cancellationToken);
}
