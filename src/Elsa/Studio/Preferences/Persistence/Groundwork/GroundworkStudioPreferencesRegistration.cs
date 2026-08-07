using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class GroundworkStudioPreferencesRegistration
{
    public static IServiceCollection AddGroundworkStudioPreferences(this IServiceCollection services)
    {
        services.AddGroundworkManifestSource<StudioPreferencesGroundworkStorageManifestSource>();
        services.RemoveAll<IStudioPreferenceStore>();
        services.AddScoped<IStudioPreferenceStore, GroundworkStudioPreferenceStore>();
        return services;
    }
}
