using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow-definition-level input removed from the Draft. Published by
/// <c>IRemoveWorkflowInputFromDraftCommand</c>.
/// </summary>
public sealed class OnWorkflowInputRemovedFromDraft(string draftId, string inputReferenceKey) : IEvent
{
    public string DraftId { get; } = draftId;
    public string InputReferenceKey { get; } = inputReferenceKey;
}
