using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListActivityStructuresHandler(IEnumerable<IActivityStructureHandler> structureHandlers)
    : IRequestHandler<ListActivityStructures, ActivityStructuresResponse>
{
    public Task<ActivityStructuresResponse> Handle(
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
