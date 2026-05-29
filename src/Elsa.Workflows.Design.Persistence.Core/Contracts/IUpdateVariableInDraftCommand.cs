using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching variable in <c>WorkflowDefinitionState.Variables</c>. Publishes
/// <c>OnVariableUpdatedInDraft</c>.
/// </summary>
public interface IUpdateVariableInDraftCommand
{
    Task Execute(string draftId, string variableReferenceKey, VariableDefinition newValue, CancellationToken cancellationToken = default);
}
