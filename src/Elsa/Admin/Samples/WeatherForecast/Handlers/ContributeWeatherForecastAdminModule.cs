using Elsa.Admin.Core.Events;
using Elsa.Admin.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Admin.Samples.WeatherForecast.Handlers;

public sealed class ContributeWeatherForecastAdminModule : IEventHandler<OnAdminModuleManifestsCollecting>
{
    public Task Handle(OnAdminModuleManifestsCollecting @event, CancellationToken cancellationToken)
    {
        @event.Manifests.Add(new AdminModuleManifest(
            "Elsa.Admin.Samples.WeatherForecast",
            "Weather Forecast Sample",
            "1.0.0",
            "/_content/Elsa.Admin.Samples.WeatherForecast/admin/modules/weather/module.js",
            ["/_content/Elsa.Admin.Samples.WeatherForecast/admin/modules/weather/module.css"],
            "^1.0.0",
            "^1.0.0",
            ["weather", "server-api"]));

        return Task.CompletedTask;
    }
}
