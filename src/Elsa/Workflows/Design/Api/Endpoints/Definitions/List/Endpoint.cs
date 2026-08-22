using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.List;

[Get("definitions")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint : RequestEndpoint<ListDefinitions, WorkflowDefinitionListView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsList";
        options.Accepts = ["*/*", "application/json"];
    }
}
