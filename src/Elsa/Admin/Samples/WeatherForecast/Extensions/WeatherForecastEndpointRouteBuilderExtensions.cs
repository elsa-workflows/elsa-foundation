using Elsa.Admin.Samples.WeatherForecast.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Admin.Samples.WeatherForecast.Extensions;

public static class WeatherForecastEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWeatherForecastAdminSample(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/_elsa/samples/weather-forecast",
            () => Results.Ok(WeatherForecastSampleData.GetForecasts()));

        return endpoints;
    }
}
