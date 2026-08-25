using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class GroundworkStudioPreferencesRegistration
{
    public static IServiceCollection AddGroundworkStudioPreferences(
        this IServiceCollection services,
        string? targetName = null)
    {
        services.AddGroundworkStorageUnit(StudioPreferencesGroundworkStorageSchema.CreateUnit(), targetName);
        services.RemoveAll<IStudioPreferenceStore>();
        services.AddScoped<IStudioPreferenceStore>(provider => new GroundworkStudioPreferenceStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            targetName));
        return services;
    }
}
