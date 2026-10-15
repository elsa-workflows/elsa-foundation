namespace Elsa.Integrations.Sdk.OAuth;

internal static class OAuth2ClientSources
{
    public const string Valence = "valence";
    public const string BringYourOwn = "byo";
}

internal static class OAuth2GrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string ClientCredentials = "client_credentials";
    public const string RefreshToken = "refresh_token";
}

/// <summary>
/// Secret names use the foundation name alphabet (letters, numbers, dot, dash, underscore, colon).
/// </summary>
internal static class OAuth2SecretKeys
{
    public static string Grant(string connectionName) => $"connection:{connectionName}:oauth2-grant";

    public static string ClientSecret(string connectionName) => $"connection:{connectionName}:client-secret";

    public static string PkceVerifier(string state) => $"oauth2:pkce:{state}";
}
