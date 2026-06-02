using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Lifecycle event for a Draft cloned from an existing Version. Published by
/// <c>ICloneDraftFromVersionCommand</c> after the new Draft + its layout sibling are
/// persisted. Per Unit C FR-018a lifecycle bullet.
/// </summary>
public sealed class OnDraftClonedFromVersion(
    string newDraftId,
    string sourceVersionId,
    string targetDefinitionId) : IEvent
{
    public string NewDraftId { get; } = newDraftId;
    public string SourceVersionId { get; } = sourceVersionId;
    public string TargetDefinitionId { get; } = targetDefinitionId;
}
