using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions.Get;

[Get("versions/{versionId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowVersionDetailsReader reader, IEnumerable<IWorkflowDefinitionAuthorizer> authorizers) : ApiEndpoint<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<WorkflowDefinitionVersionDetailsView> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        if (request.VersionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Synthetic draft identifiers are not persisted workflow definition versions.", nameof(request));

        // Same order as the definition GET (#3723): the reader's not-found response comes first, then the owning
        // definition authorizer decides on the version's definition. No details leave until that decision succeeds.
        var details = await reader.ReadAsync(request.VersionId, cancellationToken);
        var current = authorizers.ToArray();
        if (current.Length != 1 || !await current[0].CanReadAsync(details.Definition.Id, cancellationToken))
            throw new WorkflowLibraryAuthorizationException("The requested workflow definition is not readable in the current application context.");
        return details;
    }
}
