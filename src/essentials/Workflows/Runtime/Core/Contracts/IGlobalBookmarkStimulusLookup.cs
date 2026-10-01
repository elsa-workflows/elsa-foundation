using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Finds every waiting bookmark across all workflow executions that matches an external stimulus.
/// This is the cross-execution counterpart of <see cref="IBookmarkStimulusLookup"/> and is what
/// lets a single stimulus fan in to N waiting instances.
/// </summary>
public interface IGlobalBookmarkStimulusLookup
{
    ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingAsync(
        GlobalBookmarkStimulusLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every non-expired waiting bookmark of a given stimulus type across all workflow executions
    /// (spec 089 D), regardless of hash, and returns the matched <see cref="GlobalBookmarkStimulusLookupResult.Matches"/>
    /// snapshots (incl. <c>Metadata</c>) so a consumer can read the durable route template + endpoint options a
    /// mid-flow suspension stored. Expiry filtering lives here in the lookup layer — the raw
    /// <see cref="IBookmarkStimulusIndex"/> scan stays unfiltered per its documented contract. This is the
    /// type-scoped counterpart of <see cref="FindWaitingAsync"/>; there is no correlation scoping (mid-flow
    /// bookmark resumes are instance-scoped).
    /// </summary>
    ValueTask<GlobalBookmarkStimulusLookupResult> FindWaitingByTypeAsync(
        GlobalBookmarkStimulusTypeLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct stimulus hashes of the bookmarks <see cref="FindWaitingByTypeAsync"/> would match, without
    /// returning the bookmarks. A per-node projection rebuilt from waiting bookmarks (the HTTP route table) compares
    /// the result between reads to notice a suspension or resumption on another node. The default reduces
    /// <see cref="FindWaitingByTypeAsync"/>; the default lookup projects the hashes in the index instead.
    /// </summary>
    async ValueTask<IReadOnlyCollection<string>> FindWaitingStimulusHashesByTypeAsync(
        GlobalBookmarkStimulusTypeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var waiting = await FindWaitingByTypeAsync(request, cancellationToken);
        return waiting.Matches.Select(bookmark => bookmark.StimulusHash).Distinct(StringComparer.Ordinal).ToArray();
    }
}
