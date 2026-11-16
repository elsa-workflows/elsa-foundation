using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitOutboxProcessor
{
    bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => sourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint;
    /// <summary>Explicit support for selecting and draining this exact source kind's subscription protocol. Legacy processors refuse.</summary>
    bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) => false;
    ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default);
}
