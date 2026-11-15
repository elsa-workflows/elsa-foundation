using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow-definition-level output removed from the Draft. Published by
/// <c>IRemoveWorkflowOutputFromDraftCommand</c>.
/// </summary>
public sealed class WorkflowOutputRemovedFromDraft(string draftId, string outputReferenceKey) : IEvent
{
    public string DraftId { get; } = draftId;
    public string OutputReferenceKey { get; } = outputReferenceKey;
}
