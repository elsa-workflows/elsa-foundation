using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional provider-owned occurrence projection of the same atomically committed history.
/// Membership, ordinal count, exact anchor and rows refer to one authorized frozen window.
/// Tokens bind the run, scope, access context and bounds; callers must still authorize every read.
/// Providers must preserve first-observation identity through replay and retention, and invalidate
/// a window rather than silently rebase its ordinals. This port grants no execution authority.
/// </summary>
public interface IRuntimeHistoryOccurrenceStore
{
    /// <summary>Admits a window, or reads a bounded ordinal/anchor page in an existing window.</summary>
    /// <exception cref="RuntimeHistoryUnavailableException">A normalized provider failure preserves its cause and read context.</exception>
    ValueTask<RuntimeHistoryOccurrencePage> ReadOccurrencesAsync(RuntimeHistoryOccurrenceQuery query,
        CancellationToken cancellationToken = default);
}
