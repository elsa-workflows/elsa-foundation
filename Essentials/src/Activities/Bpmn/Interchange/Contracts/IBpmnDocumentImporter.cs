using Elsa.Activities.Bpmn.Interchange.Models;

namespace Elsa.Activities.Bpmn.Interchange.Contracts;

/// <summary>
/// Imports BPMN 2.0 XML documents into the native <c>elsa.bpmn.structure</c> authored payload.
/// <see cref="Analyze"/> reports what maps cleanly before anything is created (analyze-then-commit,
/// mirroring the Elsa3 reusable import flow); <see cref="Import"/> produces a <c>BpmnProcess</c>
/// activity node.
/// </summary>
public interface IBpmnDocumentImporter
{
    BpmnImportAnalysis Analyze(string xml, BpmnImportOptions? options = null);

    BpmnImportResult Import(string xml, BpmnImportOptions? options = null);
}
