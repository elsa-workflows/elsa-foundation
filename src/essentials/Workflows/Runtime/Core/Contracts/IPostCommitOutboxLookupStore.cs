using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Additive read capability for locating an exact committed outbox item regardless of delivery status.
/// </summary>
public interface IPostCommitOutboxLookupStore
{
    ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default);
}
