using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Lifecycle event for a discarded Draft (terminal entry on the Draft's event stream).
/// Published by <c>IDiscardDraftCommand</c> after the Draft + its sibling rows (layout +
/// validation) are atomically deleted per Unit C FR-029. Per FR-018a lifecycle bullet.
/// </summary>
public sealed class OnDraftDiscarded(string draftId, string workflowDefinitionId) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;
}
