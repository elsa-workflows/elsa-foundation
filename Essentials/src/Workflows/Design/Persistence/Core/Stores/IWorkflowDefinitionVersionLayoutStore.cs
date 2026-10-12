using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="WorkflowDefinitionVersionLayout"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;WorkflowDefinitionVersionLayout&gt;</c> surface with a
/// small set of intent-revealing operations a non-relational provider can also satisfy.
/// </summary>
public interface IWorkflowDefinitionVersionLayoutStore
{
    /// <summary>Finds the layout for the given version, or <c>null</c> if none exists.</summary>
    Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default);
}
