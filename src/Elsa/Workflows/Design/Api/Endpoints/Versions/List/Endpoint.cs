using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions.List;

[Get("definitions/{definitionId}/versions")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowDefinitionVersionStore store)
    : ApiEndpoint<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionSummary>>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsList";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<IEnumerable<WorkflowDefinitionVersionSummary>> HandleAsync(
        ListDefinitionVersions request,
        CancellationToken cancellationToken)
    {
        var versions = await store.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return versions.Select(e => new WorkflowDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt));
    }
}
