using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Completes one fenced outbox claim, optional dispatch projection, and optional follow-up atomically, and reports which
/// outcome it persisted. Throws <see cref="Exceptions.RuntimePostCommitOutboxStaleClaimException"/> when the presented
/// owner or fence no longer owns the current claim.
/// </summary>
public interface IRuntimePostCommitOutboxClaimCompletionStore
{
    /// <summary>Qualifies this exact physical store tuple and recognized typed participant before source effects.</summary>
    /// <remarks>Legacy providers refuse by default. A true result requires actual shared transaction/scope ownership,
    /// installed family/version/plan validation and admitted writable schemas; a connection-string match is insufficient.</remarks>
    bool CanFinalizeSource(RuntimeDeliveryBoundary boundary, RuntimePostCommitSourceFinalization finalization) => false;

    ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default);
}
