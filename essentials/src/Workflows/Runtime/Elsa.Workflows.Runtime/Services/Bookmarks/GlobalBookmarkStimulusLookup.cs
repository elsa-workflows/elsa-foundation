using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

/// <summary>
/// Default <see cref="IGlobalBookmarkStimulusLookup"/>. It queries the cross-execution
/// <see cref="IBookmarkStimulusIndex"/> for the stimulus hash, then applies the same matchability rules
/// as the per-execution <see cref="BookmarkStimulusLookup"/> — exact stimulus-type match and non-expiry —
/// plus optional correlation scoping. Results are ordered deterministically so fan-in dispatch is stable.
/// </summary>
public sealed class GlobalBookmarkStimulusLookup : IGlobalBookmarkStimulusLookup
{
    private readonly IBookmarkStimulusIndex _bookmarkStimulusIndex;
    private readonly IWorkflowExecutionStateStore? _executions;

    public GlobalBookmarkStimulusLookup(IBookmarkStimulusIndex bookmarkStimulusIndex)
        : this(bookmarkStimulusIndex, executions: null)
    {
    }

    public GlobalBookmarkStimulusLookup(IBookmarkStimulusIndex bookmarkStimulusIndex, IWorkflowExecutionStateStore? executions)
    {
        ArgumentNullException.ThrowIfNull(bookmarkStimulusIndex);
        _bookmarkStimulusIndex = bookmarkStimulusIndex;
        _executions = executions;
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

        return new GlobalBookmarkStimulusLookupResult(await ExcludeTestRunsAsync(matches, cancellationToken));
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

        return new GlobalBookmarkStimulusLookupResult(await ExcludeTestRunsAsync(matches, cancellationToken));
    }

    public async ValueTask<IReadOnlyCollection<string>> FindWaitingStimulusHashesByTypeAsync(
        GlobalBookmarkStimulusTypeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A raw hash projection has no execution classification. Use the same filtered population as route
        // discovery so an isolated draft wait cannot advertise a production HTTP route.
        var waiting = await FindWaitingByTypeAsync(request, cancellationToken);
        return StimulusHashes.DistinctOrdinal(waiting.Matches.Select(bookmark => bookmark.StimulusHash));
    }

    private async ValueTask<IReadOnlyList<BookmarkState>> ExcludeTestRunsAsync(
        IReadOnlyList<BookmarkState> bookmarks,
        CancellationToken cancellationToken)
    {
        // The optional dependency preserves direct construction for legacy callers. Normal runtime composition
        // always supplies the authoritative execution store; caller-authored bookmark metadata is never authority.
        if (_executions is null || bookmarks.Count == 0)
            return bookmarks;
        var isolated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var executionId in bookmarks.Select(bookmark => bookmark.WorkflowExecutionId).Distinct(StringComparer.Ordinal))
        {
            var execution = await _executions.FindAsync(executionId, cancellationToken);
            if (execution is { RunKind: WorkflowRunKind.TestRun } or { TestScope: not null })
                isolated.Add(executionId);
        }
        return bookmarks.Where(bookmark => !isolated.Contains(bookmark.WorkflowExecutionId)).ToArray();
    }

    private static bool Matches(BookmarkState bookmark, GlobalBookmarkStimulusLookupRequest request) =>
        StringComparer.Ordinal.Equals(bookmark.StimulusType, request.StimulusType) &&
        StringComparer.Ordinal.Equals(bookmark.StimulusHash, request.StimulusHash) &&
        (bookmark.ExpiresAt is null || bookmark.ExpiresAt > request.EvaluatedAt) &&
        GlobalBookmarkStimulusLookupResult.CorrelationMatches(bookmark, request.CorrelationId);
}
