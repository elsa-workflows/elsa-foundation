using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Endpoints.Structures.List;

[Get("structures")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IEnumerable<IActivityStructureHandler> structureHandlers)
    : ApiEndpoint<ListActivityStructures, ActivityStructuresResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "StructuresList";

    public override Task<ActivityStructuresResponse> HandleAsync(
        ListActivityStructures request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = structureHandlers
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .Select(handler => new ActivityStructureView(
                handler.Kind,
                handler.SchemaVersion,
                handler.SupportsScopedVariables,
                handler.AuthoredPayloadType is { } payloadType
                    ? AuthoringSchemaExporter.ExportSchema(payloadType)
                    : null))
            .ToArray();
        var fingerprint = AuthoringSchemaExporter.ComputeFingerprint(items);
        return Task.FromResult(new ActivityStructuresResponse(items, fingerprint));
    }
}
