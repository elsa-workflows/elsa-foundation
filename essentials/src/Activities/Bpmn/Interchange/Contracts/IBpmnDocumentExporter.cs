using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Contracts;

/// <summary>
/// Exports a <c>BpmnProcess</c> activity node (authored <c>elsa.bpmn.structure</c> payload) as a
/// BPMN 2.0 XML document with BPMNDI. Layout comes from the authored <c>diagram</c> payload when
/// present; otherwise a simple grid is synthesized so other modelers can open the file. Complete-source
/// schema2 returns its retained XML unchanged; use the asynchronous result to see omitted Elsa configuration.
/// </summary>
/// <remarks>Scoped single-implementation replacement contract; shell startup rejects multiple registrations.</remarks>
public interface IBpmnDocumentExporter
{
    /// <summary>Writes the document without any task binding: there is no catalog to say which bindings may travel.</summary>
    string Export(ActivityNode processNode, BpmnExportOptions? options = null);

    /// <summary>
    /// Writes the document, carrying each task's exact binding in <c>elsa:taskBinding</c> when the visible catalog declares
    /// every bound input a portable literal, and reports every binding left out. Complete-source export
    /// preserves XML bytes and reports disjoint native/scope/process configuration without embedding it.
    /// </summary>
    Task<BpmnExportResult> ExportAsync(ActivityNode processNode, BpmnExportOptions? options = null, CancellationToken cancellationToken = default);
}
