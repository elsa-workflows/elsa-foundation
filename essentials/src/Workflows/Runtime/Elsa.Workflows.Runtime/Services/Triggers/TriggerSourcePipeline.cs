using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Current selected delivery services qualify fresh acceptance, never retained source authority.</summary>
internal static class TriggerSourcePipeline
{
    internal static bool Supports(IRuntimePostCommitOutboxProcessor? processor, IRuntimePostCommitIntentDispatcher? dispatcher,
        RuntimeDeliverySourceKind sourceKind, string deliveryKind) =>
        processor is not null && dispatcher is not null && processor.SupportsSourceKind(sourceKind) && dispatcher.SupportsSourceKind(sourceKind) &&
        processor.SupportsSubscriptionDelivery(sourceKind, deliveryKind, RuntimeSubscriptionDelivery.CurrentVersion) &&
        dispatcher.SupportsSubscriptionDelivery(sourceKind, deliveryKind, RuntimeSubscriptionDelivery.CurrentVersion);
}
