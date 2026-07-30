using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Completes one fenced outbox claim, optional dispatch projection, and optional follow-up atomically.
/// Throws <see cref="Exceptions.RuntimePostCommitOutboxStaleClaimException"/> when the presented owner or
/// fence no longer owns the current claim.
/// </summary>
public interface IRuntimePostCommitOutboxClaimCompletionStore
{
    ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default);
}
