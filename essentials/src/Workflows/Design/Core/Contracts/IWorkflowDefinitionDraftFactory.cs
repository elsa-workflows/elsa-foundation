using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IWorkflowDefinitionDraft"/> instances (generating the id). The
/// persistence layer turns the result into its concrete entity via the entity's <c>From</c> method.
/// </summary>
public interface IWorkflowDefinitionDraftFactory
{
    IWorkflowDefinitionDraft Create(string workflowDefinitionId, WorkflowDefinitionState state, string? id = null);
}
