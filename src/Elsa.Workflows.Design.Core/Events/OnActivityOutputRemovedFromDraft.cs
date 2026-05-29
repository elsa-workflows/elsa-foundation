using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an output removed from a placed activity. Published by
/// <c>IRemoveActivityOutputFromDraftCommand</c>.
/// </summary>
public sealed class OnActivityOutputRemovedFromDraft(string draftId, string nodeId, string outputReferenceKey) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public string OutputReferenceKey { get; } = outputReferenceKey;
}
