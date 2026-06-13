using Elsa.Admin.Samples.WeatherForecast.Services;
using Xunit;

namespace Elsa.Admin.Tests;

public sealed class WeatherForecastSampleTests
{
    [Fact]
    public void Forecasts_are_deterministic()
    {
        var forecasts = WeatherForecastSampleData.GetForecasts();

        Assert.Equal(5, forecasts.Count);
        Assert.Contains(forecasts, x => x.Date == new DateOnly(2026, 6, 15) && x.Summary == "Mild");
    }
}
