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

    /// <summary>
    /// Stops <c>RequireStableOpenApi()</c> from rejecting collectible API Explorer-facing metadata,
    /// for a host that does not register an OpenAPI document service.
    /// </summary>
    /// <remarks>
    /// Suppression is only correct where nothing builds an OpenAPI document: the retention the
    /// boundary guards against is created by API Explorer's host-lifetime caches, not by the endpoint
    /// metadata itself. A suppressed endpoint carries no <see cref="OpenApiLifetimeMetadata"/> marker,
    /// because nothing verified it. Compiler-only handler metadata is still stripped, since an
    /// async state machine pins its owner regardless of whether a document is ever produced.
    /// </remarks>
    public static IServiceCollection SuppressOpenApiLifetimeEnforcement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<OpenApiLifetimeEnforcementOptions>(options => options.Enabled = false);
        return services;
    }
}
