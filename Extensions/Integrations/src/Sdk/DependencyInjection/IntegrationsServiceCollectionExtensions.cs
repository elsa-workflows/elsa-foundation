using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Integrations.Sdk.DependencyInjection;

/// <summary>
/// Runtime registration for connector activities. Activities only: no shell feature, hosted service,
/// or EF context (#2726 rule 9). Credential inputs resolve through the host's
/// <c>ISecretValueResolver</c> / <c>IRuntimeSecretResolver</c>; this method does not register either.
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds a delegating handler on the named <see cref="HttpActivityConstants.HttpClientName"/>
    /// client so <see cref="Http.IntegrationsHttpAuthScope"/> can apply per-request auth. Outbound
    /// logging stays that client's header-free logger (spec 188 FR-019), owned by
    /// <c>ActivitiesHttpFeature</c>; this method does not replace it.
    /// </summary>
    /// <remarks>
    /// #2729 will own the <c>http</c> connection authenticator on this same named client. Rebase by
    /// folding this handler into that pipeline rather than registering two.
    /// </remarks>
    public static IHttpClientBuilder AddElsaIntegrationsRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IntegrationsHttpAuthHandler>();
        return services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .AddHttpMessageHandler<IntegrationsHttpAuthHandler>();
    }
}
