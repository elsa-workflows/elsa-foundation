using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Updates the matching workflow-definition-level input in <c>WorkflowDefinitionState.Inputs</c>.
/// Publishes <c>OnWorkflowInputUpdatedInDraft</c>.
/// </summary>
public interface IUpdateWorkflowInputInDraftCommand
{
    Task Execute(string draftId, string inputReferenceKey, InputDefinition newValue, CancellationToken cancellationToken = default);
}
