using Elsa.Integrations.Sdk.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.Slack;

/// <summary>
/// Registers Slack connector activities. Auth stays on the host's foundation <c>http</c>
/// connection; compose <c>AddOAuth2Connector()</c> when the connection uses the generic
/// <c>oauth2</c> scheme. This package does not reference the OAuth2 assembly.
/// </summary>
public static class SlackConnectorExtensions
{
    public static IServiceCollection AddSlackConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaIntegrationsRuntime();
        return services;
    }
}
