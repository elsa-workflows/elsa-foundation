using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Completes one fenced outbox claim, optional dispatch projection, and optional follow-up atomically.</summary>
public interface IRuntimePostCommitOutboxClaimCompletionStore
{
    ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default);
}
