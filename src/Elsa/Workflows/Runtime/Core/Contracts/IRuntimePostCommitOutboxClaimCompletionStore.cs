using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Completes one fenced outbox claim and optional dispatch failure projection atomically.</summary>
public interface IRuntimePostCommitOutboxClaimCompletionStore
{
    ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default);
}
