using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class BookmarkStimulusLookup : IBookmarkStimulusLookup
{
    private readonly IBookmarkStateStore _bookmarkStateStore;

    public BookmarkStimulusLookup(IBookmarkStateStore bookmarkStateStore)
    {
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);

        _bookmarkStateStore = bookmarkStateStore;
    }

    public async ValueTask<BookmarkStimulusLookupResult> FindAsync(BookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bookmarks = await _bookmarkStateStore.ListAllBookmarkStatesAsync(request.WorkflowExecutionId, cancellationToken);
        var matches = bookmarks
            .Where(bookmark => Matches(bookmark, request))
            .OrderBy(bookmark => bookmark.CreatedAt)
            .ThenBy(bookmark => bookmark.BookmarkId, StringComparer.Ordinal)
            .ToArray();

        return matches.Length switch
        {
            0 => new BookmarkStimulusLookupResult(
                BookmarkStimulusLookupStatus.NotFound,
                reason: $"No active bookmark matched stimulus '{request.StimulusType}'/'{request.StimulusHash}' for workflow execution '{request.WorkflowExecutionId}'."),
            1 => new BookmarkStimulusLookupResult(BookmarkStimulusLookupStatus.Found, matches[0]),
            _ => new BookmarkStimulusLookupResult(
                BookmarkStimulusLookupStatus.Ambiguous,
                reason: $"Multiple active bookmarks matched stimulus '{request.StimulusType}'/'{request.StimulusHash}' for workflow execution '{request.WorkflowExecutionId}'.",
                ambiguousBookmarkIds: matches.Select(bookmark => bookmark.BookmarkId).ToArray())
        };
    }

    private static bool Matches(BookmarkState bookmark, BookmarkStimulusLookupRequest request) =>
        StringComparer.Ordinal.Equals(bookmark.WorkflowExecutionId, request.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(bookmark.StimulusType, request.StimulusType) &&
        StringComparer.Ordinal.Equals(bookmark.StimulusHash, request.StimulusHash) &&
        (bookmark.ExpiresAt is null || bookmark.ExpiresAt > request.EvaluatedAt);
}
