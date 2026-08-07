using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Contributes a lane's storage-manifest source and records which target owns it. Lane registrations call
/// this instead of adding the manifest source directly, so composition can admit each target over only the
/// lanes bound to it.
/// </summary>
public static class GroundworkManifestBindingRegistration
{
    /// <summary>Gets the host's manifest bindings, creating and registering them on first use.</summary>
    public static GroundworkManifestBindings GroundworkManifestBindings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkManifestBindings))
            .Select(descriptor => descriptor.ImplementationInstance as GroundworkManifestBindings)
            .FirstOrDefault(bindings => bindings is not null);
        if (existing is not null)
            return existing;

        var created = new GroundworkManifestBindings();
        services.AddSingleton(created);
        // Cross-lane operations ask which target a lane landed on; everything else is handed its store.
        services.TryAddSingleton<GroundworkLaneTargets>();
        return created;
    }

    /// <summary>
    /// Registers <typeparamref name="TSource"/> as a manifest contribution owned by
    /// <paramref name="targetName"/>. Omitting the target binds the lane to the default target.
    /// </summary>
    public static IServiceCollection AddGroundworkManifestSource<TSource>(
        this IServiceCollection services,
        string? targetName = null)
        where TSource : class, IGroundworkStorageManifestSource
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GroundworkManifestBindings().Bind(typeof(TSource), targetName);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, TSource>());
        return services;
    }
}
