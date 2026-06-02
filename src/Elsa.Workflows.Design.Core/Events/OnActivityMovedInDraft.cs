using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Layout-mutation event for a position/size change on a placed activity. Folds into the same
/// Draft event stream per Unit C FR-017 (single stream, single replay) â€” layout is part of
/// authoring per Elsa Â§E2.9.1, not a mutable side-channel. Published by
/// <c>IMoveActivityInDraftCommand</c>; the command mutates
/// <c>WorkflowDefinitionDraftLayout.Records</c>, NOT <c>WorkflowDefinitionState</c>.
/// </summary>
public sealed class OnActivityMovedInDraft(
    string draftId,
    string nodeId,
    double newX,
    double newY,
    double? newWidth = null,
    double? newHeight = null) : IEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public double NewX { get; } = newX;
    public double NewY { get; } = newY;
    public double? NewWidth { get; } = newWidth;
    public double? NewHeight { get; } = newHeight;
}
