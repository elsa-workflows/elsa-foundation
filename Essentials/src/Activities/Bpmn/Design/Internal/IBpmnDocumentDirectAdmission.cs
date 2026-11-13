using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Optional direct-publication content assessment for an entire physical workflow. It admits an
/// immutable authoring version only; the ordinary native compiler remains independently closed.
/// </summary>
/// <remarks>Optional replacement contract: at most one scoped implementation per shell, validated at startup.</remarks>
internal interface IBpmnDocumentDirectAdmission
{
    bool CanPromote(WorkflowDefinitionState state);
}
