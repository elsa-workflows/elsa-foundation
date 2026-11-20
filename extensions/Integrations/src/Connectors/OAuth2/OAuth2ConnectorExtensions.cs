using Elsa.Connections.Core.Contracts;
using Elsa.Integrations.Sdk.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Logging;

namespace Elsa.Integrations.Connectors.OAuth2;

public static class OAuth2ConnectorExtensions
{
    public static IServiceCollection AddOAuth2Connector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaIntegrationsRuntime();
        // Token-endpoint HTTP only. Do not add activity or connection handlers here.
        // AllowAutoRedirect is off so a 307/308 cannot replay the token POST (form body included).
        services.AddHttpClient(OAuth2HttpClientNames.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.TryAddScoped<OAuth2TokenService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, OAuth2SchemeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IHttpRequestAuthenticator, OAuth2HttpRequestAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOAuthCallbackHandler, OAuth2CallbackHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOAuthAuthorizeBeginHandler, OAuth2AuthorizeBeginHandler>());
        return services;
    }
}
