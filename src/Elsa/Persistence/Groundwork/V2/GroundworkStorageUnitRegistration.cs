using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Composition;

public static class GroundworkStorageUnitServiceCollectionExtensions
{
    /// <summary>
    /// Records that <typeparamref name="TLane"/> is owned by <paramref name="targetName"/> without
    /// contributing a composed host manifest. A lane declaring its storage units directly against the
    /// public v2 catalog owns its own schema, but cross-lane callers still have to resolve which target
    /// holds it. Omitting the target binds the lane to the default one.
    /// </summary>
    public static IServiceCollection AddGroundworkStorageLane<TLane>(
        this IServiceCollection services,
        string? targetName = null)
        where TLane : class, IGroundworkStorageLane
    {
        ArgumentNullException.ThrowIfNull(services);
        FindOrAddManifestBindings(services).Bind(typeof(TLane), targetName);
        return services;
    }

    /// <summary>Gets the host's lane bindings, registering them on first use.</summary>
    public static GroundworkManifestBindings FindOrAddManifestBindings(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkManifestBindings))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkManifestBindings>()
            .FirstOrDefault();
        if (existing is not null)
            return existing;

        var created = new GroundworkManifestBindings();
        services.AddSingleton(created);
        return created;
    }

    public static IServiceCollection AddGroundworkStorageUnit(
        this IServiceCollection services,
        StorageUnit unit,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(unit);

        var registry = FindOrAddRegistry(services);
        registry.Declare(unit, targetName);
        AddRuntimeOnce(services);
        return services;
    }

    private static GroundworkStorageUnitRegistry FindOrAddRegistry(IServiceCollection services)
    {
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkStorageUnitRegistry>()
            .SingleOrDefault();
        if (existing is not null)
            return existing;

        var created = new GroundworkStorageUnitRegistry();
        services.AddSingleton(created);
        return created;
    }

    private static void AddRuntimeOnce(IServiceCollection services)
    {
        services.TryAddSingleton<GroundworkPrivilegedQueryAuditSink>();
        services.TryAddSingleton<IGroundworkPrivilegedQueryAuditSink>(provider =>
            provider.GetRequiredService<GroundworkPrivilegedQueryAuditSink>());
        services.TryAddScoped<GroundworkPrivilegedQueryAuditExecutor>();

        if (services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkStorageSessionSource)))
            return;

        services.AddSingleton<GroundworkStorageSessionSource>();
        services.AddSingleton<IGroundworkStorageSessionSource>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
        services.AddSingleton<IShellInitializer>(provider =>
            provider.GetRequiredService<GroundworkStorageSessionSource>());
    }
}
