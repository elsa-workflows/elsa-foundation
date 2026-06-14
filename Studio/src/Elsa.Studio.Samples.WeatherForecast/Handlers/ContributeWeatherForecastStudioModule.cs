using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Samples.WeatherForecast.Handlers;

public sealed class ContributeWeatherForecastStudioModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
{
    public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new StudioModuleManifest(
            "Elsa.Studio.Samples.WeatherForecast",
            "Weather forecast sample",
            "1.0.0",
            "/_content/Elsa.Studio.Samples.WeatherForecast/studio/modules/weather/module.js",
            ["/_content/Elsa.Studio.Samples.WeatherForecast/studio/modules/weather/module.css"],
            "^1.0.0",
            "^1.0.0",
            ["navigation", "routes", "http"],
            [StudioBackendCapabilities.WeatherForecastSample]));

        return Task.CompletedTask;
    }
}
