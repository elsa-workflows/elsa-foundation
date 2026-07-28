using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IGlobalBookmarkStimulusLookup"/>. It queries the cross-execution
/// <see cref="IBookmarkStimulusIndex"/> for the stimulus hash, then applies the same matchability rules
/// as the per-execution <see cref="BookmarkStimulusLookup"/> — exact stimulus-type match and non-expiry —
/// plus optional correlation scoping. Results are ordered deterministically so fan-in dispatch is stable.
/// </summary>
public sealed class GlobalBookmarkStimulusLookup : IGlobalBookmarkStimulusLookup
{
    private readonly IBookmarkStimulusIndex _bookmarkStimulusIndex;

    public GlobalBookmarkStimulusLookup(IBookmarkStimulusIndex bookmarkStimulusIndex)
    {
        ArgumentNullException.ThrowIfNull(bookmarkStimulusIndex);
        _bookmarkStimulusIndex = bookmarkStimulusIndex;
    }

    public async ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingAsync(
        GlobalBookmarkStimulusLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = await _bookmarkStimulusIndex.ListAllByStimulusAsync(
            request.StimulusType,
            request.StimulusHash,
            cancellationToken);

        var matches = candidates
            .Where(bookmark => Matches(bookmark, request))
            .OrderBy(bookmark => bookmark.CreatedAt)
            .ThenBy(bookmark => bookmark.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(bookmark => bookmark.BookmarkId, StringComparer.Ordinal)
            .ToArray();

        return new GlobalBookmarkStimulusLookupResult(matches);
    }

    public async ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingByTypeAsync(
        GlobalBookmarkStimulusTypeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = await _bookmarkStimulusIndex.ListAllByStimulusTypeAsync(
            request.StimulusType,
            cancellationToken);

        // Expiry filtering stays in the lookup layer (the raw index scan is deliberately unfiltered). No
        // correlation scoping: mid-flow bookmark resumes are instance-scoped. Deterministic ordering matches the
        // hash-scoped path so consumers see a stable fan-in order.
        var matches = candidates
            .Where(bookmark =>
                StringComparer.Ordinal.Equals(bookmark.StimulusType, request.StimulusType) &&
                (bookmark.ExpiresAt is null || bookmark.ExpiresAt > request.EvaluatedAt))
            .OrderBy(bookmark => bookmark.CreatedAt)
            .ThenBy(bookmark => bookmark.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(bookmark => bookmark.BookmarkId, StringComparer.Ordinal)
            .ToArray();

        return new GlobalBookmarkStimulusLookupResult(matches);
    }

    private static bool Matches(BookmarkState bookmark, GlobalBookmarkStimulusLookupRequest request) =>
        StringComparer.Ordinal.Equals(bookmark.StimulusType, request.StimulusType) &&
        StringComparer.Ordinal.Equals(bookmark.StimulusHash, request.StimulusHash) &&
        (bookmark.ExpiresAt is null || bookmark.ExpiresAt > request.EvaluatedAt) &&
        GlobalBookmarkStimulusLookupResult.CorrelationMatches(bookmark, request.CorrelationId);
}
