using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Tagging.Core.Extensions;

public static class TaggingServiceCollectionExtensions
{
    public static IServiceCollection AddTagging(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITagDefinitionStore, InMemoryTagDefinitionStore>();
        services.TryAddScoped<ITagDefinitionAuditStore, NullTagDefinitionAuditStore>();
        services.TryAddScoped<IControlledTagValueStore, InMemoryControlledTagValueStore>();
        services.TryAddScoped<IControlledTagValueAuditStore, NullControlledTagValueAuditStore>();
        services.TryAddScoped<ITagDefinitionAuditContext, SystemTagDefinitionAuditContext>();
        services.TryAddScoped<ITagDefinitionManager, DefaultTagDefinitionManager>();
        services.TryAddScoped<IControlledTagValueManager, DefaultControlledTagValueManager>();
        return services;
    }
}
