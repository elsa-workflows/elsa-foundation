namespace Elsa.Studio.Core.Events;

public sealed class OnBackendCapabilitiesCollecting : IStudioEvent
{
    public ICollection<string> CapabilityIds { get; } = [];
}
