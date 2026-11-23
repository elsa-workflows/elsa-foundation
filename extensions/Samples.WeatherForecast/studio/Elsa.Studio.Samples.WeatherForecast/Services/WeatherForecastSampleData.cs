using Elsa.Studio.Samples.WeatherForecast.Models;

namespace Elsa.Studio.Samples.WeatherForecast.Services;

public static class WeatherForecastSampleData
{
    public static IReadOnlyCollection<WeatherForecastView> GetForecasts() =>
    [
        new(new DateOnly(2026, 6, 15), 21, "Mild"),
        new(new DateOnly(2026, 6, 16), 24, "Sunny"),
        new(new DateOnly(2026, 6, 17), 19, "Cloudy"),
        new(new DateOnly(2026, 6, 18), 17, "Rain"),
        new(new DateOnly(2026, 6, 19), 22, "Clear")
    ];
}

