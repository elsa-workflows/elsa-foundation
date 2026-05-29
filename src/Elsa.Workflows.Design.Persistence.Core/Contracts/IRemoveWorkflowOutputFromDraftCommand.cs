namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching workflow-definition-level output from <c>WorkflowDefinitionState.Outputs</c>.
/// Publishes <c>OnWorkflowOutputRemovedFromDraft</c>.
/// </summary>
public interface IRemoveWorkflowOutputFromDraftCommand
{
    Task Execute(string draftId, string outputReferenceKey, CancellationToken cancellationToken = default);
}
