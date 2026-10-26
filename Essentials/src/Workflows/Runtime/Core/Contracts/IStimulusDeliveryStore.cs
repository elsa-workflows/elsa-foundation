using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider replacement capability for an existing source outbox row's immutable recipient snapshot.</summary>
/// <remarks>
/// One scoped implementation per provider. Install atomically validates exact persisted intent/scope and current
/// owner/fence and the authoritative publisher's tenant/partition/run/test-scope facts, then chooses absent-to-whole
/// snapshot under row revision CAS. Return the persisted winner, never an
/// uncommitted losing proposal. Every later row rewrite preserves that winner. A missing/legacy ambiguous source,
/// corrupt state, unavailable provider or lost authority must refuse before effects; no per-attempt fallback exists.
/// Read an existing snapshot through the same provider's exact outbox lookup. This contract creates no second ledger.
/// </remarks>
public interface IStimulusDeliveryStore
{
    /// <exception cref="StimulusDeliveryBoundaryException">
    /// The exact persisted source, publisher scope or snapshot cannot be read/validated. Include its source ID and
    /// preserve the original cause. Temporary dependency failures, including unrequested provider cancellation,
    /// set IsUnavailable; malformed/conflicting/unsupported legacy state does not. No failed installation accepts effects.
    /// </exception>
    /// <exception cref="RuntimePostCommitOutboxStaleClaimException">
    /// Current persisted owner/fence no longer admits this source claim. Preserve the source for its rightful owner.
    /// </exception>
    /// <exception cref="OperationCanceledException">The supplied cancellation token was requested; propagate it unchanged.</exception>
    /// <exception cref="ArgumentException">The caller supplied invalid source/candidate arguments.</exception>
    ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate,
        CancellationToken cancellationToken = default);
}
