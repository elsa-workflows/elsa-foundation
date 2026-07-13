using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Provider-owned bounded query plan for workflow execution history. Implementations must execute filtering,
/// counting, keyset navigation and page limits in the persistence provider.
/// </summary>
public interface IGroundworkWorkflowExecutionStatePageQuery
{
    ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default);
}
