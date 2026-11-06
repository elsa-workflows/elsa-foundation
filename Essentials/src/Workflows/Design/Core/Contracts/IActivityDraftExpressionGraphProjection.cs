using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// A scoped, keyed strategy projecting one provider's already authorized persisted activity draft.
/// Register one strategy per nonblank, ordinal provider key; duplicate keys fail shell startup.
/// Only the strategy matching the persisted provider key runs. Missing keys or unsupported manifests
/// produce no context. This design-time projection is never a persisted workflow.
/// </summary>
public interface IActivityDraftExpressionGraphProjection
{
    string ProviderKey { get; }
    WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState state);
}
