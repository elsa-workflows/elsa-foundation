using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an input removed from a placed activity. Published by
/// <c>IRemoveActivityInputFromDraftCommand</c>.
/// </summary>
public sealed class OnActivityInputRemovedFromDraft(string draftId, string nodeId, string inputReferenceKey) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public string InputReferenceKey { get; } = inputReferenceKey;
}
