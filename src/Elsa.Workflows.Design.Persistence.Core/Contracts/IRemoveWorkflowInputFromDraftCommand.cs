namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching workflow-definition-level input from <c>WorkflowDefinitionState.Inputs</c>.
/// Publishes <c>OnWorkflowInputRemovedFromDraft</c>.
/// </summary>
public interface IRemoveWorkflowInputFromDraftCommand
{
    Task Execute(string draftId, string inputReferenceKey, CancellationToken cancellationToken = default);
}
