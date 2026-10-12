using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Explicit complete traversals over finite cross-execution bookmark-index pages.
/// </summary>
public static class BookmarkStimulusIndexPagingExtensions
{
    public static ValueTask<IReadOnlyCollection<BookmarkState>> ListAllByStimulusAsync(
        this IBookmarkStimulusIndex index,
        string stimulusType,
        string stimulusHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        return TraverseAsync(
            continuationToken => index.ListByStimulusPageAsync(
                new BookmarkStimulusPageQuery(
                    stimulusType,
                    stimulusHash,
                    continuationToken: continuationToken),
                cancellationToken));
    }

    public static ValueTask<IReadOnlyCollection<BookmarkState>> ListAllByStimulusTypeAsync(
        this IBookmarkStimulusIndex index,
        string stimulusType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        return TraverseAsync(
            continuationToken => index.ListByStimulusTypePageAsync(
                new BookmarkStimulusTypePageQuery(
                    stimulusType,
                    continuationToken: continuationToken),
                cancellationToken));
    }

    private static async ValueTask<IReadOnlyCollection<BookmarkState>> TraverseAsync(
        Func<string?, ValueTask<RuntimeStorePage<BookmarkState>>> readPage)
    {
        var items = new List<BookmarkState>();
        string? continuationToken = null;
        do
        {
            var page = await readPage(continuationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }
}
