using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a <b>workflow-definition-level</b> output declared on the Draft.
/// Distinct from <c>OnActivityPropertyChangedInDraft</c> (per-activity) â€” workflow-level
/// outputs only get bound as activity-shaped outputs at compile time when the workflow is
/// composed as an activity inside another workflow. Published by
/// <c>IAddWorkflowOutputToDraftCommand</c>. Per Unit C FR-018 workflow-outputs bullet.
/// </summary>
public sealed class OnWorkflowOutputAddedToDraft(string draftId, OutputDefinition output) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public OutputDefinition Output { get; } = output;
}
