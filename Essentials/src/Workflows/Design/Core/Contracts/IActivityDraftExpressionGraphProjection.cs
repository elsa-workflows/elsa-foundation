using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// A provider contributes its authoring graph for an already authorized persisted activity draft.
/// Return null for unsupported providers or manifests. This projection is never a persisted workflow.
/// </summary>
public interface IActivityDraftExpressionGraphProjection
{
    WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState state);
}
