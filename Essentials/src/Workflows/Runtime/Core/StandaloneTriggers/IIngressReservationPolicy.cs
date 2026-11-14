namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Source-owned semantic collision rule for exclusive reservations of one original stimulus type.</summary>
/// <remarks>
/// The selected physical provider invokes the one registered owner inside its resource/publication write boundary,
/// against current reservations and active legacy bindings. This does not replace the original stimulus identity.
/// </remarks>
public interface IIngressReservationPolicy
{
    string StimulusType { get; }

    /// <summary>Compares two original claims of this type; malformed routing facts must refuse the write.</summary>
    /// <exception cref="TriggerOperationException">Persisted routing facts are malformed or unavailable.</exception>
    bool Conflicts(RuntimeIngressReservationDescriptor first, RuntimeIngressReservationDescriptor second);
}
