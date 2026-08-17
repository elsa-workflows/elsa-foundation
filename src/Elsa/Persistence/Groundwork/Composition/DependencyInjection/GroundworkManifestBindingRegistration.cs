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
        // The bindings themselves live with the v2 catalog, because a lane that declares its units
        // directly has to be able to bind itself without taking the document-store closure. Resolving
        // them over that seam keeps one instance whichever side registered first.
        var bindings = GroundworkStorageUnitServiceCollectionExtensions.FindOrAddManifestBindings(services);
        // Cross-lane operations ask which target a lane landed on; everything else is handed its store.
        services.TryAddSingleton<GroundworkLaneTargets>();
        // ...and the few that write several lanes need each lane's store, not just its target name.
        // Scoped, because it hands back the scoped per-target document stores: a singleton holding the
        // root provider would resolve them from the root and capture them outside any request scope.
        services.TryAddScoped<GroundworkLaneStores>();
        return bindings;
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
