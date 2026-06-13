using Elsa.Admin.Samples.WeatherForecast.Models;
using Elsa.Admin.Samples.WeatherForecast.Services;
using Elsa.Api.FastEndpoints.Abstractions;

namespace Elsa.Admin.Samples.WeatherForecast.Endpoints;

internal sealed class GetWeatherForecast : ElsaEndpointWithoutRequest<IReadOnlyCollection<WeatherForecastView>>
{
    public override void Configure()
    {
        Get("/_elsa/samples/weather-forecast");
        ConfigurePermissions();
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        return Send.OkAsync(WeatherForecastSampleData.GetForecasts(), ct);
    }
}
