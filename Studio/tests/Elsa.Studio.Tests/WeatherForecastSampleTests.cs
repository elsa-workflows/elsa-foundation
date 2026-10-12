using Elsa.Studio.Samples.WeatherForecast.Services;

namespace Elsa.Studio.Tests;

public sealed class WeatherForecastSampleTests
{
    [Fact]
    public void GetForecasts_ReturnsDeterministicSampleData()
    {
        var forecasts = WeatherForecastSampleData.GetForecasts().ToList();

        Assert.Equal(5, forecasts.Count);
        Assert.Equal(new DateOnly(2026, 6, 15), forecasts[0].Date);
        Assert.Equal(21, forecasts[0].TemperatureC);
        Assert.Equal("Mild", forecasts[0].Summary);
    }
}

