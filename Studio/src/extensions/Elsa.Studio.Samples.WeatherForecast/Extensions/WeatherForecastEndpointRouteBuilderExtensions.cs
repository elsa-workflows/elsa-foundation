using Elsa.Studio.Samples.WeatherForecast.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Studio.Samples.WeatherForecast.Extensions;

public static class WeatherForecastEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWeatherForecastStudioSample(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/studio/samples/weather-forecast", () => Results.Ok(WeatherForecastSampleData.GetForecasts()));
        return endpoints;
    }
}

