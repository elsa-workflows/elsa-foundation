using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;

[Post("definitions")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<AddDefinition, WorkflowDefinitionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAdd";
        options.Accepts = ["application/json"];
    }
}
