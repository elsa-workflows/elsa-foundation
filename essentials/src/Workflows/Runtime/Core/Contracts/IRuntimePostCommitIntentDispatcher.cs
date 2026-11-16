using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitIntentDispatcher
{
    ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default);
    /// <summary>Legacy dispatchers have no required-source contributions.</summary>
    bool RequiresDurableSource(string intentKind) => false;
    bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => sourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint;
    /// <summary>Explicit support from the actual contributed handler for this exact subscription delivery protocol.</summary>
    bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) => false;

    /// <summary>Default refusal preserves legacy dispatchers without fabricating a workflow intent.</summary>
    ValueTask DispatchAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.DeliveryKind,
            new NotSupportedException("The selected dispatcher does not support this source family."), false));

    /// <summary>The additive context path requires explicit dispatcher support; legacy dispatchers retain their intent-only path.</summary>
    /// <remarks>The context is not independent authorization; a consumer revalidates its domain/provider boundary before effects.</remarks>
    /// <exception cref="RuntimePostCommitSourceBoundaryException">The selected dispatcher cannot preserve a required source; delivery remains owed.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates; no intent-only fallback is attempted.</exception>
    ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
            new NotSupportedException("The selected dispatcher does not carry durable source context."), false));
}
