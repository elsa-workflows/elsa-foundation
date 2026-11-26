using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Composition;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;

public static class IdentityPersistenceAuthorityServiceCollectionExtensions
{
    /// <summary>
    /// Selects the feature that owns ASP.NET Core Identity persistence and installs final-composition
    /// validation for options resolution, plain-host startup, and shell initialization.
    /// </summary>
    public static IServiceCollection SelectIdentityPersistenceAuthority(
        this IServiceCollection services,
        string featureName,
        params IdentityAuthorityCompatibility[] compatibilityClassifiers)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        ArgumentNullException.ThrowIfNull(compatibilityClassifiers);

        var guard = services
            .Where(descriptor => descriptor.ServiceType == typeof(IdentityPersistenceAuthorityGuard))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityPersistenceAuthorityGuard>()
            .SingleOrDefault();
        var isNewGuard = guard is null;
        guard ??= new IdentityPersistenceAuthorityGuard(services);
        guard.AddCompatibilityClassifiers(compatibilityClassifiers);
        guard.EnsureCanSelect(featureName);

        if (isNewGuard)
        {
            services.AddSingleton(guard);
            services.AddSingleton<IConfigureOptions<IdentityOptions>>(sp =>
                sp.GetRequiredService<IdentityPersistenceAuthorityGuard>());
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredService<IdentityPersistenceAuthorityGuard>());
            services.AddSingleton<IShellInitializer>(sp =>
                sp.GetRequiredService<IdentityPersistenceAuthorityGuard>());
        }

        var alreadySelected = services
            .Where(descriptor => descriptor.ServiceType == typeof(IdentityPersistenceAuthority))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityPersistenceAuthority>()
            .Any(selection => string.Equals(selection.FeatureName, featureName, StringComparison.Ordinal));
        if (!alreadySelected)
            services.AddSingleton(new IdentityPersistenceAuthority(featureName));

        guard.Validate();
        return services;
    }
}
