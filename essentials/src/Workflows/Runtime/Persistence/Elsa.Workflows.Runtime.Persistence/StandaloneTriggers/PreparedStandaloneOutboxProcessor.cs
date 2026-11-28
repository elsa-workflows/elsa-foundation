using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class PreparedStandaloneOutboxProcessor(IRuntimePostCommitOutboxProcessor processor) : IRuntimePostCommitOutboxProcessor
{
    public bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) =>
        sourceKind == RuntimeDeliverySourceKind.StandaloneIngress && processor.SupportsSourceKind(sourceKind);

    public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) =>
        deliveryKind == RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind && SupportsSourceKind(sourceKind) &&
        processor.SupportsSubscriptionDelivery(sourceKind, deliveryKind, protocolVersion);

    public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkflowExecutionId is not null ||
            request.IntentKind is { } kind && kind != RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind)
            throw new NotSupportedException("This Runtime owner processes only standalone Actor Foundation source deliveries.");
        return processor.ProcessAsync(new(request.Limit, intentKind: RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind), cancellationToken);
    }
}
