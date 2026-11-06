using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Core.Contracts;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints.Documents.Capabilities;

/// <summary>Authoring retention support, independent from current runtime execution admission.</summary>
[Get("interchange/bpmn/authoring-capabilities")]
[RequirePermission(BpmnInterchangePermissions.Read)]
public sealed class Endpoint(IEnumerable<IActivityStructureHandler> handlers) : ApiEndpointWithoutRequest<BpmnDocumentAuthoringCapabilities>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetBpmnDocumentAuthoringCapabilities";

    public override Task<BpmnDocumentAuthoringCapabilities> HandleAsync(CancellationToken cancellationToken)
    {
        var source = handlers.Where(handler => handler.Kind == BpmnActivityIdentities.StructureKind &&
            handler.SchemaVersion == BpmnAuthoredDocument.SchemaVersion).ToArray();
        if (source.Length != 1)
            throw new InvalidOperationException("Complete BPMN authoring requires exactly one matching source structure handler.");
        return Task.FromResult(new BpmnDocumentAuthoringCapabilities(source[0].Kind, source[0].SchemaVersion));
    }
}
