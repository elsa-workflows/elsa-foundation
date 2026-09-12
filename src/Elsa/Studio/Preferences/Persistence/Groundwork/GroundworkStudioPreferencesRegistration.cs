using Elsa.Persistence.Groundwork.Composition;
using Elsa.Studio.Preferences.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

public static class GroundworkStudioPreferencesRegistration
{
    public const string StoreBackendName = "groundwork";

    public static IServiceCollection AddGroundworkStudioPreferences(
        this IServiceCollection services,
        string? targetName = null)
    {
        var existingBackend = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<StudioPreferenceStoreBackend>()
            .FirstOrDefault();
        StudioPreferenceStoreBackend.EnsureCompatible(existingBackend?.Name, StoreBackendName);
        if (existingBackend is null)
            services.AddSingleton(new StudioPreferenceStoreBackend(StoreBackendName));
        services.AddGroundworkStorageUnit(StudioPreferencesGroundworkStorageSchema.CreateUnit(), targetName);
        services.RemoveAll<IStudioPreferenceStore>();
        services.AddScoped<IStudioPreferenceStore>(provider => new GroundworkStudioPreferenceStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            targetName));
        return services;
    }
}
