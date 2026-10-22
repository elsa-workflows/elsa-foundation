using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// One authentication scheme (connections plan D3) for the foundation <c>http</c> connection.
/// </summary>
public sealed record OAuth2AuthenticationScheme
{
    public const string Name = HttpAuthSchemes.OAuth2;
    public const bool RequiresWritableStore = true;
    public const string ClientSourceValence = OAuth2ClientSources.Valence;
    public const string ClientSourceBringYourOwn = OAuth2ClientSources.BringYourOwn;
    public const string GrantTypeAuthorizationCode = OAuth2GrantTypes.AuthorizationCode;
    public const string GrantTypeClientCredentials = OAuth2GrantTypes.ClientCredentials;

    public required string GrantType { get; init; }
    public string? AuthorizationUrl { get; init; }
    public required string TokenUrl { get; init; }
    public string? ScopesUrl { get; init; }
    public string? Scopes { get; init; }
    public string ClientSource { get; init; } = ClientSourceValence;
    public string? ValenceAppId { get; init; }
    public string? ClientId { get; init; }

    public IReadOnlyDictionary<string, string> ToConnectionSettings()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OAuth2SettingKeys.GrantType] = GrantType,
            [OAuth2SettingKeys.TokenUrl] = TokenUrl,
            [OAuth2SettingKeys.ClientSource] = ClientSource
        };
        if (!string.IsNullOrWhiteSpace(AuthorizationUrl))
            settings[OAuth2SettingKeys.AuthorizationUrl] = AuthorizationUrl;
        if (!string.IsNullOrWhiteSpace(ScopesUrl))
            settings[OAuth2SettingKeys.ScopesUrl] = ScopesUrl;
        if (!string.IsNullOrWhiteSpace(Scopes))
            settings[OAuth2SettingKeys.Scopes] = Scopes;
        if (!string.IsNullOrWhiteSpace(ValenceAppId))
            settings[OAuth2SettingKeys.ValenceAppId] = ValenceAppId;
        if (!string.IsNullOrWhiteSpace(ClientId))
            settings[OAuth2SettingKeys.ClientId] = ClientId;
        return settings;
    }

    public void EnsureStoreCanHoldGrants(SecretStoreDescriptor store) =>
        OAuth2GrantStorePolicy.EnsureWritable(store);
}

internal sealed class OAuth2SchemeProvider : IAuthenticationSchemeProvider
{
    public AuthSchemeDescriptor Descriptor { get; } = new(
        HttpAuthSchemes.OAuth2,
        "OAuth 2.0",
        [
            new AuthSchemeField(OAuth2SettingKeys.GrantType, "Grant type", Sensitive: false, "text"),
            new AuthSchemeField(OAuth2SettingKeys.TokenUrl, "Token URL", Sensitive: false, "url"),
            new AuthSchemeField(OAuth2SettingKeys.AuthorizationUrl, "Authorization URL", Sensitive: false, "url", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.Scopes, "Scopes", Sensitive: false, "text", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.ClientSource, "Client source", Sensitive: false, "text", Required: false),
            new AuthSchemeField(OAuth2SettingKeys.ClientId, "Client ID", Sensitive: false, "text", Required: false),
            new AuthSchemeField(OAuth2SecretFields.ClientSecret, "Client secret", Sensitive: true, "password", Required: false)
        ],
        HasInteractiveAuthorization: true,
        HasRefreshableState: true,
        HasRevocation: true,
        RequiresWritableStore: true);
}
