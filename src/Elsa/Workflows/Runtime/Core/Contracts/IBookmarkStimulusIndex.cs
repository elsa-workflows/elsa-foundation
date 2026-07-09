using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Cross-execution index over durable bookmark state keyed by external stimulus identity (W7, E3-5).
/// Unlike <see cref="IBookmarkStateStore.ListAsync"/>, which is scoped to a single workflow execution,
/// this returns every bookmark waiting for a stimulus <em>across all executions</em>, which is what
/// makes external-event fan-in to N waiting instances possible. It is a narrow read contract implemented
/// by the same store that owns bookmark state, so no separate index document has to be maintained.
/// </summary>
public interface IBookmarkStimulusIndex
{
    /// <summary>
    /// Returns every bookmark whose stimulus type and hash match, across all workflow executions.
    /// Expiry and correlation filtering are applied by <see cref="IGlobalBookmarkStimulusLookup"/>, not here.
    /// </summary>
    ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusAsync(
        string stimulusType,
        string stimulusHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every bookmark of a given stimulus type across all workflow executions, regardless of hash
    /// (spec 089 D). Unlike <see cref="ListByStimulusAsync"/> this is a type-scoped scan — it lets a consumer
    /// enumerate all bookmarks waiting on a family of stimuli (e.g. every waiting <c>HttpEndpoint</c> bookmark)
    /// to union their route templates. Like the hash-scoped scan it applies no expiry or correlation filtering;
    /// that stays in <see cref="IGlobalBookmarkStimulusLookup"/> per the index's documented raw-read contract.
    /// </summary>
    ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusTypeAsync(
        string stimulusType,
        CancellationToken cancellationToken = default);
}
