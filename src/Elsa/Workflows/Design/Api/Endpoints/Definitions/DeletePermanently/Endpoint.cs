using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.DeletePermanently;

[Delete("definitions/{definitionId}/permanent")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<DeleteDefinitionPermanently>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsDeletePermanently";
        options.Accepts = ["*/*", "application/json"];
    }
}
