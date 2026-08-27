using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions.Get;

[Get("versions/{versionId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowVersionDetailsReader reader) : ApiEndpoint<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "VersionsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<WorkflowDefinitionVersionDetailsView> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        if (request.VersionId.StartsWith("draft:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Synthetic draft identifiers are not persisted workflow definition versions.", nameof(request));

        return reader.ReadAsync(request.VersionId, cancellationToken);
    }
}
