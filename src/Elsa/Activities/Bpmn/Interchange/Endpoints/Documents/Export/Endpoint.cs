using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints.Documents.Export;

[Post("interchange/bpmn/export")]
[RequirePermission(BpmnInterchangePermissions.Read)]
public sealed class Endpoint(IBpmnDocumentExporter exporter) : ApiEndpoint<ExportBpmnDocumentRequest, ExportBpmnDocumentResult>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "ExportBpmnDocumentEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override Task<ExportBpmnDocumentResult> HandleAsync(ExportBpmnDocumentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ExportBpmnDocumentResult(exporter.Export(request.ProcessNode, new BpmnExportOptions
        {
            ProcessId = request.ProcessId
        })));
}
