using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an activity removed from the canvas. Published by
/// <c>IRemoveActivityFromDraftCommand</c>. Per Unit C FR-018 activities-graph bullet.
/// </summary>
public sealed class OnActivityRemovedFromDraft(string draftId, string nodeId) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
}
