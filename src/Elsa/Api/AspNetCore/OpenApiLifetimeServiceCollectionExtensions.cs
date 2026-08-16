using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Api.AspNetCore;

/// <summary>Registers host services needed by dynamically replaceable documented endpoints.</summary>
public static class OpenApiLifetimeServiceCollectionExtensions
{
    /// <summary>
    /// Makes API Explorer regenerate its immutable description collection when the effective
    /// <see cref="Microsoft.AspNetCore.Routing.EndpointDataSource"/> publishes a new generation.
    /// </summary>
    public static IServiceCollection AddDynamicEndpointApiExplorerRefresh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IActionDescriptorChangeProvider, EndpointDataSourceActionDescriptorChangeProvider>());
        return services;
    }
}
