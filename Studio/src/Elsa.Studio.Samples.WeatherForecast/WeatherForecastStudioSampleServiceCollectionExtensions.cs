using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Samples.WeatherForecast.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Samples.WeatherForecast;

public static class WeatherForecastStudioSampleServiceCollectionExtensions
{
    public static IServiceCollection AddWeatherForecastStudioSample(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeWeatherForecastStudioModule>();
    }
}

