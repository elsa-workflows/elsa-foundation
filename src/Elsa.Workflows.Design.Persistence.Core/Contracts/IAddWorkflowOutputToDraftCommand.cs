using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Appends a workflow-definition-level output to <c>WorkflowDefinitionState.Outputs</c>.
/// Distinct from per-activity outputs (which mutate via <c>IAddActivityOutputToDraftCommand</c>).
/// Publishes <c>OnWorkflowOutputAddedToDraft</c>.
/// </summary>
public interface IAddWorkflowOutputToDraftCommand
{
    Task Execute(string draftId, OutputDefinition output, CancellationToken cancellationToken = default);
}
