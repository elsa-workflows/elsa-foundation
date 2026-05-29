using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Lifecycle-origination command that creates a fresh <c>WorkflowDefinitionDraft</c> with
/// the supplied (or empty) State and a corresponding empty <c>WorkflowDefinitionDraftLayout</c>.
/// Publishes <c>OnDraftCreated</c>. Per Unit C FR-019 lifecycle bullet.
/// </summary>
public interface ICreateDraftCommand
{
    Task<string> Execute(string workflowDefinitionId, WorkflowDefinitionState? initialState = null, CancellationToken cancellationToken = default);
}
