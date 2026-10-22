using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

public static class OAuth2GrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string ClientCredentials = "client_credentials";
    public const string RefreshToken = "refresh_token";
}

public static class OAuth2SettingKeys
{
    public const string GrantType = "grantType";
    public const string AuthorizationUrl = "authorizationUrl";
    public const string TokenUrl = "tokenUrl";
    public const string ScopesUrl = "scopesUrl";
    public const string Scopes = "scopes";
    public const string ClientId = "clientId";
}

public static class OAuth2SecretFields
{
    public const string ClientSecret = "clientSecret";
}

internal static class OAuth2SecretKeys
{
    public static string Grant(string connectionName) => $"connection:{connectionName}:oauth2-grant";

    public static string Pkce(string connectionName, string nonce) =>
        $"connection:{connectionName}:oauth2-pkce:{nonce}";
}

internal static class OAuth2GrantStorePolicy
{
    public static readonly TimeSpan PkceLifetime = TimeSpan.FromMinutes(10);

    public static void EnsureWritable(SecretStoreDescriptor store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var writable = !store.IsReadOnly && store.Capabilities.HasFlag(SecretStoreCapabilities.Write);
        if (!writable)
            throw new InvalidOperationException(
                $"OAuth2 grants require a writable secret store. '{store.Name}' is read-only (RequiresWritableStore).");
    }
}
