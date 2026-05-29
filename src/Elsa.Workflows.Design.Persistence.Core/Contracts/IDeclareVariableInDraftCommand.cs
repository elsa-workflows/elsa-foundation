using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends a workflow variable to <c>WorkflowDefinitionState.Variables</c>. Publishes
/// <c>OnVariableDeclaredInDraft</c>.
/// </summary>
public interface IDeclareVariableInDraftCommand
{
    Task Execute(string draftId, VariableDefinition variable, CancellationToken cancellationToken = default);
}
