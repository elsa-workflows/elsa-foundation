using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.UpdateMetadata;

[Patch("definitions/{definitionId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsUpdate";
        options.Accepts = ["application/json"];
    }
}
