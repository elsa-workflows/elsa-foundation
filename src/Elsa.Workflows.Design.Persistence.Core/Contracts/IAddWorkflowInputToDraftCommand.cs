using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends a workflow-definition-level input to <c>WorkflowDefinitionState.Inputs</c>.
/// Distinct from per-activity inputs (which mutate via <c>IAddActivityInputToDraftCommand</c>).
/// Publishes <c>OnWorkflowInputAddedToDraft</c>.
/// </summary>
public interface IAddWorkflowInputToDraftCommand
{
    Task Execute(string draftId, InputDefinition input, CancellationToken cancellationToken = default);
}
