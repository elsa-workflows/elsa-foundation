using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Lifecycle-origination event for a freshly-created <c>WorkflowDefinitionDraft</c>. Published by
/// <c>ICreateDraftCommand</c> after the Draft is persisted but before the command returns. Per
/// Unit C FR-018 lifecycle bullet. Sealed class per framework Â§2.6.1.
/// </summary>
public sealed class OnDraftCreated(string draftId, string workflowDefinitionId) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;
}
