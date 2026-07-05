using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="WorkflowDefinitionDraft"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;WorkflowDefinitionDraft&gt;</c> surface with a small
/// set of intent-revealing operations a non-relational provider can also satisfy.
/// </summary>
public interface IWorkflowDefinitionDraftStore
{
    /// <summary>Finds the draft with the given id, or <c>null</c> if it does not exist.</summary>
    Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default);

    /// <summary>Finds the current draft owned by the given workflow definition, or <c>null</c> if none exists.</summary>
    Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Lists all drafts owned by the given workflow definition.</summary>
    Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Finds the complete designer layout records for the draft.</summary>
    Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default);
}
