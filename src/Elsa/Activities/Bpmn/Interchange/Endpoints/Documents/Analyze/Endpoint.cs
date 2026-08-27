using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints.Documents.Analyze;

[Post("interchange/bpmn/analyze")]
[RequirePermission(BpmnInterchangePermissions.Read)]
public sealed class Endpoint(IBpmnDocumentImporter importer) : ApiEndpoint<AnalyzeBpmnDocumentRequest, BpmnImportAnalysis>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AnalyzeBpmnDocumentEndpoint";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.Required;
    }

    public override Task<BpmnImportAnalysis> HandleAsync(AnalyzeBpmnDocumentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(importer.Analyze(request.Xml, new BpmnImportOptions { ProcessId = request.ProcessId }));
}
