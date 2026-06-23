using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="WorkflowDefinitionDraft"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;WorkflowDefinitionDraft&gt;</c> surface with a small
/// set of intent-revealing operations a non-relational provider can also satisfy.
/// </summary>
public interface IWorkflowDefinitionDraftStore
{
    /// <summary>Finds the draft owned by the given workflow definition, or <c>null</c> if none exists.</summary>
    Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);
}
