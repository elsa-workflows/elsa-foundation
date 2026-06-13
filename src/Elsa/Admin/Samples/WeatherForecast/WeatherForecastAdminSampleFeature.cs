using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Events.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Admin.Samples.WeatherForecast;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Admin")]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: "AdminSamplesWeatherForecast",
    Description = "Sample server-backed admin module that contributes a weather forecast endpoint and UI."
)]
public class WeatherForecastAdminSampleFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddEventHandlersFrom(GetType().Assembly);
    }
}
