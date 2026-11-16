using Elsa.Integrations.Sdk.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.Microsoft365;

/// <summary>
/// Registers Microsoft 365 connector activities. Auth stays on the host's foundation <c>http</c>
/// connection; compose <c>AddOAuth2Connector()</c> when the connection uses the generic
/// <c>oauth2</c> scheme. This package does not reference the OAuth2 assembly.
/// </summary>
public static class Microsoft365ConnectorExtensions
{
    public static IServiceCollection AddMicrosoft365Connector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaIntegrationsRuntime();
        return services;
    }
}
