using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints.Documents.Import;

[Post("interchange/bpmn/import")]
[RequirePermission(BpmnInterchangePermissions.Manage)]
public sealed class Endpoint(IBpmnDocumentImporter importer) : ApiEndpoint<ImportBpmnDocumentRequest, BpmnImportResult>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ImportBpmnDocumentEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override Task<BpmnImportResult> HandleAsync(ImportBpmnDocumentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(importer.Import(request.Xml, new BpmnImportOptions
        {
            ProcessId = request.ProcessId,
            NodeIdPrefix = request.NodeIdPrefix
        }));
}
