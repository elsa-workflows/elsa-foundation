using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Explicit complete traversals over finite workflow-execution history pages. Callers that require every
/// retained execution opt into this helper instead of causing a provider to issue an unbounded collection read.
/// </summary>
public static class WorkflowExecutionStateStorePagingExtensions
{
    public const int MaximumPageSize = 500;

    public static async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAllAsync(
        this IWorkflowExecutionStateStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var items = new List<WorkflowExecutionState>();
        string? cursor = null;
        do
        {
            var page = await store.QueryPageAsync(
                new WorkflowExecutionStatePageQuery(MaximumPageSize, Cursor: cursor),
                cancellationToken);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return items;
    }
}
