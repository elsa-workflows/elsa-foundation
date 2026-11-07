using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Reads the exact BPMN owner and semantic consumption of a retained native service-work origin.</summary>
/// <remarks>The reader uses accepted runtime material and physical receipts. A missing owner or source is unavailable,
/// never proof of terminality. Returned addresses keep logical process identity separate from material identity.</remarks>
public interface IBpmnNativeWorkConsumer
{
    /// <exception cref="BpmnModelValidationException">The retained origin, source, binding or complete material pin is invalid.</exception>
    /// <exception cref="RuntimeSchedulerWorkUnavailableException">The exact retained model material or physical evidence is unavailable.</exception>
    ValueTask<BpmnNativeWorkAddress> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default);

    /// <exception cref="BpmnModelValidationException">The retained owner or consumption evidence does not match its exact origin.</exception>
    /// <exception cref="RuntimeSchedulerWorkUnavailableException">The exact retained model material or physical evidence is unavailable.</exception>
    ValueTask<BpmnNativeWorkObservation> InspectAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default);
}

/// <summary>Optional read-only validation immediately before a live native result advances the BPMN owner.</summary>
/// <remarks>Contributors must not write a second store, grant continuation permissions or acknowledge delivery here.
/// Exact prior-input replay, obsolete/terminal rejection and cancellation settlement do not invoke this contribution.</remarks>
public interface IBpmnNativeWorkResultValidator
{
    ValueTask ValidateAsync(BpmnNativeWorkResultValidation context, CancellationToken cancellationToken = default);
}

/// <summary>Optional settlement after an exact model input receipt and before its existing native-result source is delivered.</summary>
/// <remarks>Contributors may settle their own durable responsibility after verifying semantic consumption or positive
/// root terminality. A rejected transport receipt alone is not consumption. Failure leaves the same source owed;
/// contributors must not introduce another worker source that waits for this result's consumption.</remarks>
public interface IBpmnNativeWorkResultDeliveryContributor
{
    ValueTask CompleteAsync(BpmnNativeWorkResultDelivery context, CancellationToken cancellationToken = default);
}
