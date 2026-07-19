using Elsa.Persistence.Groundwork.Composition;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Tagging.Persistence.Groundwork.DependencyInjection;

public static class GroundworkTaggingStoreRegistration
{
    public static IServiceCollection AddGroundworkTaggingStore(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, TaggingGroundworkStorageManifestSource>());
        services.RemoveAll<ITagDefinitionStore>();
        services.RemoveAll<ITagDefinitionAuditStore>();
        services.RemoveAll<IControlledTagValueStore>();
        services.RemoveAll<IControlledTagValueAuditStore>();
        services.AddScoped<GroundworkTagDefinitionStore>();
        services.AddScoped<ITagDefinitionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkTagDefinitionStore>());
        services.AddScoped<ITagDefinitionAuditStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkTagDefinitionStore>());
        services.AddScoped<ITagDefinitionAtomicChangeStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkTagDefinitionStore>());
        services.AddScoped<GroundworkControlledTagValueStore>();
        services.AddScoped<IControlledTagValueStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkControlledTagValueStore>());
        services.AddScoped<IControlledTagValueAuditStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkControlledTagValueStore>());
        services.AddScoped<IControlledTagValueAtomicChangeStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkControlledTagValueStore>());
        services.TryAddSingleton<ITagDefinitionCatalogPersistence, GroundworkTagDefinitionCatalogPersistence>();
        services.TryAddSingleton<IControlledTagValueCatalogPersistence, GroundworkControlledTagValueCatalogPersistence>();
        return services;
    }

    private sealed class GroundworkTagDefinitionCatalogPersistence : ITagDefinitionCatalogPersistence;
    private sealed class GroundworkControlledTagValueCatalogPersistence : IControlledTagValueCatalogPersistence;
}
