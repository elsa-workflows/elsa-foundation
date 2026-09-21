using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a <b>workflow-definition-level</b> output declared on the Draft.
/// Distinct from <c>ActivityPropertyChangedInDraft</c> (per-activity) â€” workflow-level
/// outputs only get bound as activity-shaped outputs at compile time when the workflow is
/// composed as an activity inside another workflow. Published by
/// <c>IAddWorkflowOutputToDraftCommand</c>. Per Unit C FR-018 workflow-outputs bullet.
/// </summary>
public sealed class WorkflowOutputAddedToDraft(string draftId, OutputDefinition output) : IEvent
{
    public string DraftId { get; } = draftId;
    public OutputDefinition Output { get; } = output;
}
