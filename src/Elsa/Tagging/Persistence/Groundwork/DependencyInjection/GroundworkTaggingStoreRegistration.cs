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
        services.AddScoped<GroundworkTagDefinitionStore>();
        services.AddScoped<ITagDefinitionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkTagDefinitionStore>());
        services.AddScoped<ITagDefinitionAuditStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkTagDefinitionStore>());
        return services;
    }
}
