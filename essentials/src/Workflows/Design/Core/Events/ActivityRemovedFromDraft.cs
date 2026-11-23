using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an activity removed from the canvas. Published by
/// <c>IRemoveActivityFromDraftCommand</c>.
/// </summary>
public sealed class ActivityRemovedFromDraft(string draftId, string nodeId) : IEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
}
