using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;

[Get("definitions/{definitionId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowDefinitionDetailsReader reader) : ApiEndpoint<GetDefinition, WorkflowDefinitionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<WorkflowDefinitionDetailsView> HandleAsync(GetDefinition request, CancellationToken cancellationToken) =>
        reader.ReadAsync(request.DefinitionId, cancellationToken);
}
