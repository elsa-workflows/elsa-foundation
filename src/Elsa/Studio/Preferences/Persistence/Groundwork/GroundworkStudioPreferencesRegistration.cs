using Elsa.Studio.Preferences.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class GroundworkStudioPreferencesRegistration
{
    public static IServiceCollection AddGroundworkStudioPreferences(this IServiceCollection services)
    {
        services.RemoveAll<IStudioPreferenceStore>();
        services.AddSingleton<IStudioPreferenceStore, GroundworkStudioPreferenceStore>();
        return services;
    }
}
