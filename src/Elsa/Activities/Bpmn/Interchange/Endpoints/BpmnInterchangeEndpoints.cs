using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

// BPMN interchange endpoints (ingestion-style, mirroring the Elsa3 reusable-import surface: no API
// capability, analyze-then-commit, read/manage permission split). All three are pure conversions —
// nothing is persisted server-side; the client applies the imported node through normal authoring.

public sealed record AnalyzeBpmnDocumentRequest(string Xml, string? ProcessId = null);

/// <summary>Inventories a BPMN 2.0 document (processes, element counts, degradations) before import.</summary>
public sealed class AnalyzeBpmnDocumentEndpoint(IBpmnDocumentImporter importer)
    : ElsaEndpoint<AnalyzeBpmnDocumentRequest, BpmnImportAnalysis>
{
    public override void Configure()
    {
        Post("interchange/bpmn/analyze");
        ConfigurePermissions(PermissionNames.BpmnInterchangeRead);
    }

    public override async Task HandleAsync(AnalyzeBpmnDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.OkAsync(importer.Analyze(request.Xml, new BpmnImportOptions { ProcessId = request.ProcessId }), cancellationToken);
        }
        catch (BpmnInterchangeException exception)
        {
            ThrowError(exception.Message, 400);
        }
    }
}

public sealed record ImportBpmnDocumentRequest(string Xml, string? ProcessId = null, string? NodeIdPrefix = null);

/// <summary>Converts a BPMN 2.0 document into a <c>BpmnProcess</c> activity node plus the analysis report.</summary>
public sealed class ImportBpmnDocumentEndpoint(IBpmnDocumentImporter importer)
    : ElsaEndpoint<ImportBpmnDocumentRequest, BpmnImportResult>
{
    public override void Configure()
    {
        Post("interchange/bpmn/import");
        ConfigurePermissions(PermissionNames.BpmnInterchangeManage);
    }

    public override async Task HandleAsync(ImportBpmnDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.OkAsync(importer.Import(request.Xml, new BpmnImportOptions
            {
                ProcessId = request.ProcessId,
                NodeIdPrefix = request.NodeIdPrefix
            }), cancellationToken);
        }
        catch (BpmnInterchangeException exception)
        {
            ThrowError(exception.Message, 400);
        }
    }
}

public sealed record ExportBpmnDocumentRequest(ActivityNode ProcessNode, string? ProcessId = null);

public sealed record ExportBpmnDocumentResult(string Xml);

/// <summary>Renders a <c>BpmnProcess</c> activity node as BPMN 2.0 XML + BPMNDI.</summary>
public sealed class ExportBpmnDocumentEndpoint(IBpmnDocumentExporter exporter)
    : ElsaEndpoint<ExportBpmnDocumentRequest, ExportBpmnDocumentResult>
{
    public override void Configure()
    {
        Post("interchange/bpmn/export");
        ConfigurePermissions(PermissionNames.BpmnInterchangeRead);
    }

    public override async Task HandleAsync(ExportBpmnDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.OkAsync(new ExportBpmnDocumentResult(exporter.Export(request.ProcessNode, new BpmnExportOptions { ProcessId = request.ProcessId })), cancellationToken);
        }
        catch (BpmnInterchangeException exception)
        {
            ThrowError(exception.Message, 400);
        }
    }
}
