using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Elsa.Studio.Samples.WeatherForecast.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Samples.WeatherForecast;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Weather")]
[StudioModule("weather", "Weather forecast sample", "1.0.1", "navigation", "routes", "http")]
[ShellFeature(
    name: "WeatherForecastSample",
    DisplayName = "Weather forecast sample module",
    Description = "Contributes the weather forecast sample Studio module manifest and its sample endpoint."
)]
public sealed class WeatherForecastStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapWeatherForecastStudioSample();
    }
}
