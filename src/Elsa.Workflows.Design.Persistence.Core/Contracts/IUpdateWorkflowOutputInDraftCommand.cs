using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching workflow-definition-level output in <c>WorkflowDefinitionState.Outputs</c>.
/// Publishes <c>OnWorkflowOutputUpdatedInDraft</c>.
/// </summary>
public interface IUpdateWorkflowOutputInDraftCommand
{
    Task Execute(string draftId, string outputReferenceKey, OutputDefinition newValue, CancellationToken cancellationToken = default);
}
