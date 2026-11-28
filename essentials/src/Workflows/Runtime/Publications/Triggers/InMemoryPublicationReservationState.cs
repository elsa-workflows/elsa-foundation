using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

// Publication and standalone ingress use the same records and lock when composed together.
internal sealed class InMemoryPublicationReservationState
{
    internal object SyncRoot { get; } = new();
    internal Dictionary<string, RuntimeIngressReservation> Reservations { get; } = new(StringComparer.Ordinal);
}

internal interface IInMemoryPublicationReservationStateSource
{
    InMemoryPublicationReservationState? PublicationReservations { get; }
}
