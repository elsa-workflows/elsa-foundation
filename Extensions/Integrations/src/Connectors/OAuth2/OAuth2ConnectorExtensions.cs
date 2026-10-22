using Elsa.Connections.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Integrations.Connectors.OAuth2;

public static class OAuth2ConnectorExtensions
{
    public static IServiceCollection AddOAuth2Connector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<OAuth2TokenService>();
        services.TryAddSingleton<IValenceOAuthAppRegistry, InMemoryValenceOAuthAppRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, OAuth2SchemeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IHttpRequestAuthenticator, OAuth2HttpRequestAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOAuthCallbackHandler, OAuth2CallbackHandler>());
        return services;
    }
}
