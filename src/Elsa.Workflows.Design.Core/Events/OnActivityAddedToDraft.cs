using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an activity placed on the canvas. Published by
/// <c>IAddActivityToDraftCommand</c> after the snapshot mutation, before <c>OnDraftValidating</c>.
/// Per Unit C FR-018 activities-graph bullet.
/// </summary>
/// <remarks>
/// The payload carries the placed <see cref="ActivityNode"/> directly. The spec named a phantom
/// <c>IActivityNodeView</c> read-only view â€” Unit C ships the existing <see cref="ActivityNode"/>
/// record (sealed, immutable by structure) instead; introducing a parallel IView interface adds
/// surface area without strengthening the non-mutating guarantee that records already provide.
/// </remarks>
public sealed class OnActivityAddedToDraft(string draftId, ActivityNode activity) : IEvent
{
    public string DraftId { get; } = draftId;
    public ActivityNode Activity { get; } = activity;
    public string NodeId => Activity.NodeId;
    public string ActivityVersionId => Activity.ActivityVersionId;
}
