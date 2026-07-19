using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public static class ActivityExecutionStateStoreExtensions
{
    public static async ValueTask<IReadOnlyList<ActivityExecutionState>> ListAllAsync(
        this IActivityExecutionStateStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<ActivityExecutionState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new ActivityExecutionStatePageQuery(workflowExecutionId, continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }

    public static async ValueTask<IReadOnlyList<ActivityExecutionState>> ListAllByParentAsync(
        this IActivityExecutionStateStore store,
        string workflowExecutionId,
        string parentActivityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<ActivityExecutionState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListByParentPageAsync(
                new ActivityExecutionStateParentPageQuery(
                    workflowExecutionId,
                    parentActivityExecutionId,
                    continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }
}
