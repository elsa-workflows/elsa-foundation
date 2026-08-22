using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Restore;

[Post("definitions/{definitionId}/restore")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint : CommandEndpoint<RestoreDefinition>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsRestore";
        // The published contract rejects any non-JSON content type with a bare 415, header included.
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.Accepts = ["application/json"];
    }
}
