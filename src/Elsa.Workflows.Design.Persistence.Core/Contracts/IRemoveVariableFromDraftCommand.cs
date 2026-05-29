namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Removes the matching variable from <c>WorkflowDefinitionState.Variables</c>. Publishes
/// <c>OnVariableRemovedFromDraft</c>.
/// </summary>
public interface IRemoveVariableFromDraftCommand
{
    Task Execute(string draftId, string variableReferenceKey, CancellationToken cancellationToken = default);
}
