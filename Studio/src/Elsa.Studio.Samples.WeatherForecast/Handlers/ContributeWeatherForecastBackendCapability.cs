using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Samples.WeatherForecast.Handlers;

public sealed class ContributeWeatherForecastBackendCapability : IStudioEventHandler<OnBackendCapabilitiesCollecting>
{
    public Task Handle(OnBackendCapabilitiesCollecting @event, CancellationToken cancellationToken)
    {
        @event.CapabilityIds.Add(StudioBackendCapabilities.WeatherForecastSample);
        return Task.CompletedTask;
    }
}
