using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
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

    public override async Task<WorkflowDefinitionDetailsView> HandleAsync(GetDefinition request, CancellationToken cancellationToken)
    {
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read,
            new WorkflowLibraryResource(DefinitionId: request.DefinitionId), cancellationToken);
        return await reader.ReadAsync(request.DefinitionId, cancellationToken);
    }
}
