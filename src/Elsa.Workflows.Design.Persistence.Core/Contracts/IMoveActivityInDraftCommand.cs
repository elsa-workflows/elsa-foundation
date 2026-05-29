namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching <c>DesignMetadataRecord</c> in <c>WorkflowDefinitionDraftLayout.Records</c>.
/// Does NOT touch <c>WorkflowDefinitionState</c> — layout is the sibling, not nested in State,
/// per Elsa §E2.9.2. Publishes <c>OnActivityMovedInDraft</c>.
/// </summary>
public interface IMoveActivityInDraftCommand
{
    Task Execute(string draftId, string nodeId, double newX, double newY, double? newWidth = null, double? newHeight = null, CancellationToken cancellationToken = default);
}
