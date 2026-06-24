using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Provider-neutral read port for <see cref="WorkflowDefinition"/>. Replaces the
/// <c>IQueryable</c>/LINQ-bound <c>IQueries&lt;WorkflowDefinition&gt;</c> surface with a small set of
/// intent-revealing operations, so any host-selected provider (EF Core, Groundwork, ...) can back it.
/// Writes continue to flow through the dedicated command contracts.
/// </summary>
public interface IWorkflowDefinitionStore
{
    /// <summary>Gets the definition with the given id, throwing if it does not exist.</summary>
    Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Finds the definition with the given id, or <c>null</c> if it does not exist.</summary>
    Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Lists the definitions matching the supplied filter.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);
}
