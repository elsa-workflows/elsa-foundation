using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Connections.Schemes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Integrations.Sdk.DependencyInjection;

/// <summary>
/// Runtime registration for connector activities. Activities only: no shell feature, hosted service,
/// or EF context (#2726 rule 9). The host (or harness) still registers <c>IConnectionLookup</c> and
/// <c>ISecretValueResolver</c>.
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the connection context factory, the open scheme authenticators this SDK ships, and a
    /// delegating handler on the named <see cref="HttpActivityConstants.HttpClientName"/> client.
    /// Outbound logging stays that client's header-free logger (spec 188 FR-019), owned by
    /// <c>ActivitiesHttpFeature</c>; this method does not replace it.
    /// </summary>
    public static IHttpClientBuilder AddElsaIntegrationsRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConnectionContextFactory, ConnectionContextFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionAuthenticator, BearerConnectionAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionAuthenticator, ApiKeyConnectionAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionAuthenticator, BasicConnectionAuthenticator>());
        services.TryAddTransient<ConnectionAuthDelegatingHandler>();
        return services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .AddHttpMessageHandler<ConnectionAuthDelegatingHandler>();
    }
}
