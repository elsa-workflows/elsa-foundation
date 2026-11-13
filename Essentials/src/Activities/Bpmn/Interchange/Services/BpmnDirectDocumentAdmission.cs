using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>Uses the exact publication projection; this optional contribution does not authorize execution or pins.</summary>
internal sealed class BpmnDirectDocumentAdmission : IBpmnDocumentDirectAdmission
{
    public bool CanPromote(WorkflowDefinitionState state)
    {
        if (state.RootActivity?.Structure is not { Kind: BpmnActivityIdentities.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion })
            return false;
        try
        {
            _ = BpmnAuthoredDirectProjection.Project(new WorkflowDefinitionVersion("direct-content-assessment", "1.0.0") { State = state }, null);
            return true;
        }
        catch (BpmnModelValidationException) { return false; }
    }
}
