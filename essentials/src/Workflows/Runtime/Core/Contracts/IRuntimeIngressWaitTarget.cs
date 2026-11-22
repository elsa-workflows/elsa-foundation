using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Ordinary durable resume boundary for exact waits retained by a true accepted external source.</summary>
public interface IRuntimeIngressWaitTarget
{
    ValueTask<RuntimeIngressWaitDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, CancellationToken cancellationToken = default);
    ValueTask<RuntimeIngressWaitReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken = default);
    ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeIngressWaitRecipient recipient,
        RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken = default);
}
