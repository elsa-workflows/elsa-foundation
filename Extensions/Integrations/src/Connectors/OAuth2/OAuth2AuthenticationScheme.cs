using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// One authentication scheme (connections plan D3) for the foundation <c>http</c> connection.
/// Client credentials are BYO and resolve through the connection's secret field, including the
/// named-credential source (<see cref="ConnectionCredentialSourceKinds.NamedCredential"/>).
/// </summary>
public sealed record OAuth2AuthenticationScheme
{
    public const string Name = HttpAuthSchemes.OAuth2;
    public const bool RequiresWritableStore = true;
    public const string GrantTypeAuthorizationCode = OAuth2GrantTypes.AuthorizationCode;
    public const string GrantTypeClientCredentials = OAuth2GrantTypes.ClientCredentials;

    public required string GrantType { get; init; }
    public string? AuthorizationUrl { get; init; }
    public required string TokenUrl { get; init; }
    public string? ScopesUrl { get; init; }
    public string? Scopes { get; init; }
    public string? ClientId { get; init; }
    public string? TokenHeaderScheme { get; init; }

    public IReadOnlyDictionary<string, string> ToConnectionSettings()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OAuth2SettingKeys.GrantType] = GrantType,
            [OAuth2SettingKeys.TokenUrl] = TokenUrl
        };
        if (!string.IsNullOrWhiteSpace(AuthorizationUrl))
            settings[OAuth2SettingKeys.AuthorizationUrl] = AuthorizationUrl;
        if (!string.IsNullOrWhiteSpace(ScopesUrl))
            settings[OAuth2SettingKeys.ScopesUrl] = ScopesUrl;
        if (!string.IsNullOrWhiteSpace(Scopes))
            settings[OAuth2SettingKeys.Scopes] = Scopes;
        if (!string.IsNullOrWhiteSpace(ClientId))
            settings[OAuth2SettingKeys.ClientId] = ClientId;
        if (!string.IsNullOrWhiteSpace(TokenHeaderScheme))
            settings[OAuth2SettingKeys.TokenHeaderScheme] = TokenHeaderScheme;
        return settings;
    }

    public void EnsureStoreCanHoldGrants(SecretStoreDescriptor store) =>
        OAuth2GrantStorePolicy.EnsureWritable(store);
}

internal sealed class OAuth2SchemeProvider(ISecretStoreRegistry stores) : IAuthenticationSchemeProvider
{
    public AuthSchemeDescriptor Descriptor { get; } = new(
        HttpAuthSchemes.OAuth2,
        "OAuth 2.0",
        [
            new AuthSchemeField(OAuth2SettingKeys.GrantType, "Grant type", Sensitive: false, "text"),
            new AuthSchemeField(OAuth2SettingKeys.TokenUrl, "Token URL", Sensitive: false, "url"),
            new AuthSchemeField(OAuth2SettingKeys.AuthorizationUrl, "Authorization URL", Sensitive: false, "url", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.Scopes, "Scopes", Sensitive: false, "text", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.ClientId, "Client ID", Sensitive: false, "text"),
            new AuthSchemeField(OAuth2SecretFields.ClientSecret, "Client secret", Sensitive: true, "password", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.TokenHeaderScheme, "Token header scheme", Sensitive: false, "text", Required: false)
        ],
        HasInteractiveAuthorization: true,
        HasRefreshableState: true,
        HasRevocation: false,
        RequiresWritableStore: true);

    public ValueTask ValidateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        _ = tenantId;
        _ = cancellationToken;
        if (!stores.TryGet(SecretStoreNames.Encrypted, out var store) || store is null)
            throw new InvalidOperationException("OAuth2 grants require a writable secret store.");
        OAuth2GrantStorePolicy.EnsureWritable(store.Descriptor);
        return ValueTask.CompletedTask;
    }
}
