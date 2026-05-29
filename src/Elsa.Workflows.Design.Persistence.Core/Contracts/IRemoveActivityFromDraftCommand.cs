namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the activity (by <paramref name="nodeId"/>) from <c>WorkflowDefinitionState.Activities</c>
/// and removes all <c>ActivityConnections</c> referencing the NodeId (no dangling connections);
/// removes the corresponding <c>DesignMetadataRecord</c> from the Draft's layout sibling.
/// Publishes <c>OnActivityRemovedFromDraft</c>.
/// </summary>
public interface IRemoveActivityFromDraftCommand
{
    Task Execute(string draftId, string nodeId, CancellationToken cancellationToken = default);
}
