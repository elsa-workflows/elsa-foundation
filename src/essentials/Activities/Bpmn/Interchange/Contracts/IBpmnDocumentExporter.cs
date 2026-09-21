using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Contracts;

/// <summary>
/// Exports a <c>BpmnProcess</c> activity node (authored <c>elsa.bpmn.structure</c> payload) as a
/// BPMN 2.0 XML document with BPMNDI. Layout comes from the authored <c>diagram</c> payload when
/// present; otherwise a simple grid is synthesized so other modelers can open the file.
/// </summary>
public interface IBpmnDocumentExporter
{
    string Export(ActivityNode processNode, BpmnExportOptions? options = null);
}
