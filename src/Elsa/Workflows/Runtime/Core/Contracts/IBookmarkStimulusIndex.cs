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
}
