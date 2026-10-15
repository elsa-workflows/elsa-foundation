namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Well-known scheme names for the authenticators this SDK ships. Connector packages may register
/// additional <see cref="IConnectionAuthenticator"/> implementations; those names are not listed here.
/// </summary>
public static class ConnectionAuthSchemes
{
    public const string ApiKey = "api-key";

    public const string Basic = "basic";

    public const string Bearer = "bearer";
}
