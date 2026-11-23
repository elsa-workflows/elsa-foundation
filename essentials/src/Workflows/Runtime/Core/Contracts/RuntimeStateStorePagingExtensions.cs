using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Explicit complete traversals over finite runtime-state store pages. Callers that truly need every
/// current item opt into the traversal rather than causing a provider to issue an unbounded read.
/// </summary>
public static class RuntimeStateStorePagingExtensions
{
    public static async ValueTask<IReadOnlyCollection<BookmarkState>> ListAllBookmarkStatesAsync(
        this IBookmarkStateStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var items = new List<BookmarkState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new BookmarkStatePageQuery(workflowExecutionId, continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }

    public static async ValueTask<IReadOnlyCollection<DurableValueState>> ListAllDurableValueStatesAsync(
        this IDurableValueStateStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var items = new List<DurableValueState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new DurableValueStatePageQuery(workflowExecutionId, continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }
}
