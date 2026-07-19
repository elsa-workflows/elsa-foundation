using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Optional bounded browsing capability for workflow definitions. Its registration signals that the active
/// persistence provider admits a deterministic, continuation-safe page query.
/// </summary>
public interface IWorkflowDefinitionPageStore
{
    Task<WorkflowDefinitionPage> QueryPageAsync(
        WorkflowDefinitionPageQuery query,
        CancellationToken cancellationToken = default);
}
