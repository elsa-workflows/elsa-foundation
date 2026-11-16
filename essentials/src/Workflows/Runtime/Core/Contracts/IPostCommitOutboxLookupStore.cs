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

    /// <summary>
    /// Finds an undelivered physically committed source owned by this execution, excluding the supplied ordinal kinds.
    /// Includes delayed, claimed, retryable and final failures from every checkpoint. Ignores working-set overlays.
    /// Null proves that all other retained sources are Delivered; it is not a due-now or claimable-source query.
    /// The caller must establish a terminal producer and independently justify every excluded kind before using absence as a settlement proof.
    /// Requires explicit scope and refuses caller-owned transactions. Legacy providers refuse this capability.
    /// </summary>
    /// <exception cref="Exceptions.RuntimeSchedulerWorkUnavailableException">Provider I/O or unrequested cancellation prevented the physical read; the cause and scope/execution context are retained.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates unchanged.</exception>
    ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId, IReadOnlyCollection<string> excludedIntentKinds,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem?>(new NotSupportedException("The selected provider cannot verify prior durable outbox responsibility."));
}
