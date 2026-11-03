using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>One exact intent kind's payload owner projects a canonical persisted intent into routing input.</summary>
/// <remarks>Register exactly one adapter per kind. The runtime never interprets an activity payload schema.
/// Projection performs no recipient effects and grants no source or recipient authority.</remarks>
public interface IStimulusDeliveryIntentAdapter
{
    string IntentKind { get; }

    /// <summary>Decode the provider's returned immutable intent, including its original key and local target.</summary>
    /// <exception cref="ArgumentNullException">The caller supplied no intent.</exception>
    /// <exception cref="Exceptions.StimulusDeliveryIntentException">The kind or owning payload schema is invalid;
    /// preserves intent identity, kind and the original decoding cause.</exception>
    StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent);
}
