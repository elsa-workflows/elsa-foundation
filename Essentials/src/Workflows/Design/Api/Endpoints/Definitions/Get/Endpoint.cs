using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;

[Get("definitions/{definitionId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowDefinitionDetailsReader reader, IEnumerable<IWorkflowDefinitionAuthorizer> authorizers) : ApiEndpoint<GetDefinition, WorkflowDefinitionDetailsView>
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
        // Preserve the owning reader's not-found response before the final replaceable owner
        // decision. No details are returned until that decision succeeds.
        var details = await reader.ReadAsync(request.DefinitionId, cancellationToken);
        var current = authorizers.ToArray();
        if (current.Length != 1 || !await current[0].CanReadAsync(request.DefinitionId, cancellationToken))
            throw new WorkflowLibraryAuthorizationException("The requested workflow definition is not readable in the current application context.");
        return details;
    }
}
