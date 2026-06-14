using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Studio.Samples.WeatherForecast.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Samples.WeatherForecast;

[ShellFeature(
    name: "WeatherForecastSample",
    DisplayName = "Weather forecast sample module",
    Description = "Contributes the weather forecast sample Studio module manifest and its sample endpoint."
)]
public sealed class WeatherForecastStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWeatherForecastStudioSample();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapWeatherForecastStudioSample();
    }
}
