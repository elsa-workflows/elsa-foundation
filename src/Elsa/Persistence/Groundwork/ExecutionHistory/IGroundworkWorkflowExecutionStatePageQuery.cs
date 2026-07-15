using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Provider-owned bounded query plan for workflow execution history. Implementations must execute filtering,
/// counting, keyset navigation and page limits in the persistence provider.
/// </summary>
public interface IGroundworkWorkflowExecutionStatePageQuery
{
    /// <summary>Binds this query to the exact admitted workflow-execution-state route.</summary>
    void Bind(ExecutableStorageRoute route);

    /// <summary>Completes read-only query preparation during host startup.</summary>
    ValueTask PrepareAsync(CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default);
}
