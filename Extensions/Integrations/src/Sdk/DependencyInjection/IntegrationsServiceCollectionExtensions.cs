using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Logging;

namespace Elsa.Integrations.Sdk.DependencyInjection;

/// <summary>
/// Runtime registration for connector activities. Activities only: no shell feature, hosted service,
/// or EF context (#2726 rule 9). Credential inputs resolve through the host's
/// <c>ISecretValueResolver</c> / <c>IRuntimeSecretResolver</c>; this method does not register either.
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    /// <summary>
    /// Ensures the named <see cref="HttpActivityConstants.HttpClientName"/> client exists with
    /// header-free outbound logging and registers the unbound <see cref="IHttpConnectionApplicator"/>
    /// when Connections is not composed. Does not add an auth <see cref="DelegatingHandler"/>;
    /// outbound credentials attach at send time through
    /// <see cref="IHttpConnectionApplicator.ApplyAsync"/>.
    /// </summary>
    public static IHttpClientBuilder AddElsaIntegrationsRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IHttpConnectionApplicator, UnboundHttpConnectionApplicator>();
        services.TryAddSingleton<HttpActivityClientLogger>();
        return services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .RemoveAllLoggers()
            .AddLogger<HttpActivityClientLogger>();
    }
}
