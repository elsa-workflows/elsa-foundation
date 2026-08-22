using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete;

[Delete("definitions/{definitionId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<SoftDeleteDefinition>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsDelete";
        options.Accepts = ["*/*", "application/json"];
    }
}
