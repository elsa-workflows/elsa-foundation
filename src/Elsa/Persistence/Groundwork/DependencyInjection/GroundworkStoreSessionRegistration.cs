using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

public static class GroundworkStoreSessionRegistration
{
    /// <summary>
    /// Registers scoped Elsa adapters over one provider-owned admitted session source, keyed by target name.
    /// <para>
    /// Every target gets its own publication chain (session source, session factory, scoped document store)
    /// under its own service key, so two targets never share a session or a transaction boundary. The
    /// <see cref="GroundworkTargetNames.Default"/> target additionally publishes the unkeyed
    /// <see cref="IDocumentStore"/>/<see cref="IBoundedDocumentStore"/> registrations, which is what lets the
    /// stores that inject a document store directly keep resolving without knowing targets exist.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGroundworkStoreSessions(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var target = GroundworkTargetNames.Normalize(targetName);
        services.AddPersistenceCore();

        // Audit plumbing is host-wide: privileged access is recorded per operation, not per store.
        services.TryAddSingleton<GroundworkPrivilegedAccessSink>();
        services.TryAddScoped<IGroundworkPrivilegedAccessEmitter, GroundworkPrivilegedAccessRecorder>();

        services.TryAddKeyedSingleton<GroundworkStoreSessionSource>(target);
        services.TryAddKeyedSingleton<IGroundworkStoreSessionSource>(
            target,
            (serviceProvider, key) => serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key));
        services.TryAddKeyedScoped<IGroundworkStoreSessionFactory>(
            target,
            (serviceProvider, key) => new GroundworkStoreSessionFactory(
                serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                serviceProvider.GetRequiredKeyedService<IGroundworkStoreSessionSource>(key),
                serviceProvider.GetService<IGroundworkPrivilegedAccessEmitter>()));
        services.TryAddKeyedScoped(
            target,
            (serviceProvider, key) => new GroundworkScopedDocumentStore(
                serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                serviceProvider.GetRequiredKeyedService<IGroundworkStoreSessionFactory>(key),
                serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key)));
        services.TryAddKeyedScoped<IDocumentStore>(
            target,
            (serviceProvider, key) => serviceProvider.GetRequiredKeyedService<GroundworkScopedDocumentStore>(key));
        services.TryAddKeyedScoped<IBoundedDocumentStore>(
            target,
            (serviceProvider, key) => serviceProvider.GetRequiredKeyedService<GroundworkScopedDocumentStore>(key));

        if (GroundworkTargetNames.IsDefault(target))
            services.PublishDefaultTargetUnkeyed(target);

        return services;
    }

    /// <summary>
    /// Aliases the default target's keyed chain onto the unkeyed service types. The document-store
    /// replacements are unconditional so a Groundwork adapter still wins over any previously registered
    /// store regardless of feature composition order, but they only remove <em>unkeyed</em> descriptors:
    /// wiping keyed ones would tear down whichever other targets were composed first.
    /// </summary>
    private static void PublishDefaultTargetUnkeyed(this IServiceCollection services, string target)
    {
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(target));
        services.TryAddSingleton<IGroundworkStoreSessionSource>(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(target));
        services.TryAddScoped(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<IGroundworkStoreSessionFactory>(target));
        services.TryAddScoped(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<GroundworkScopedDocumentStore>(target));

        services.RemoveAllUnkeyed<IDocumentStore>();
        services.RemoveAllUnkeyed<IBoundedDocumentStore>();
        services.AddScoped<IDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<GroundworkScopedDocumentStore>(target));
        services.AddScoped<IBoundedDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<GroundworkScopedDocumentStore>(target));
    }

    /// <summary>
    /// Removes only the unkeyed descriptors for <typeparamref name="TService"/>.
    /// <see cref="ServiceCollectionDescriptorExtensions.RemoveAll{T}(IServiceCollection)"/> matches on service
    /// type alone and would take every target's keyed registration with it.
    /// </summary>
    private static void RemoveAllUnkeyed<TService>(this IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService)
                services.RemoveAt(index);
        }
    }
}
