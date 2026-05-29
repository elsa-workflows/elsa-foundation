using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a <b>workflow-definition-level</b> input declared on the Draft.
/// Distinct from <c>OnActivityPropertyChangedInDraft</c> (per-activity) â€” workflow-level
/// inputs only get bound as activity-shaped inputs at compile time when the workflow is
/// composed as an activity inside another workflow. Published by
/// <c>IAddWorkflowInputToDraftCommand</c>. Per Unit C FR-018 workflow-inputs bullet.
/// </summary>
public sealed class OnWorkflowInputAddedToDraft(string draftId, InputDefinition input) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public InputDefinition Input { get; } = input;
}
