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
    /// <summary>
    /// Reads only a physically committed source in the selected provider scope. Decorators must bypass working-set
    /// overlays. A legacy provider refuses this capability; FindAsync is never a durable-source fallback.
    /// </summary>
    /// <exception cref="InvalidDataException">The source is corrupt or its original provider-scope ownership cannot be proved.</exception>
    /// <exception cref="NotSupportedException">The provider has not implemented this additive capability.</exception>
    /// <exception cref="InvalidOperationException">The caller has no explicit scope or holds a local/enlisted/ambient transaction.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates unchanged.</exception>
    ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem?>(new NotSupportedException("The selected provider cannot verify a durable outbox source."));
}
