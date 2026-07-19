using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public static class ActivityExecutionInspectionStoreExtensions
{
    public static async ValueTask<IReadOnlyList<ActivityExecutionInspectionSummaryProjection>> ListAllSummariesAsync(
        this IActivityExecutionInspectionStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<ActivityExecutionInspectionSummaryProjection>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListSummariesPageAsync(
                new ActivityExecutionInspectionSummaryPageQuery(
                    workflowExecutionId,
                    continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }
}
