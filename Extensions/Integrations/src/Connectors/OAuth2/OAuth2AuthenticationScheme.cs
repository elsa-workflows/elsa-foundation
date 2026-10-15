using Elsa.Integrations.Sdk.OAuth;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// The Generic OAuth2 connector: one authentication scheme (connections plan D3), not a parallel
/// connection model and not a second HTTP activity. <c>SendHttpRequest</c> uses the foundation
/// <c>http</c> connection (D1) with this scheme.
/// </summary>
public sealed record OAuth2AuthenticationScheme
{
    public const string Name = "oauth2";
    public const bool RequiresWritableStore = true;

    public required string GrantType { get; init; }
    public string? AuthorizationUrl { get; init; }
    public required string TokenUrl { get; init; }
    public string? ScopesUrl { get; init; }
    public string? Scopes { get; init; }
    public string ClientSource { get; init; } = OAuth2ClientSources.Valence;
    public string? ValenceAppId { get; init; }
    public string? ClientId { get; init; }

    public IReadOnlyDictionary<string, string> ToConnectionSettings()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grantType"] = GrantType,
            ["tokenUrl"] = TokenUrl,
            ["clientSource"] = ClientSource
        };
        if (!string.IsNullOrWhiteSpace(AuthorizationUrl))
            settings["authorizationUrl"] = AuthorizationUrl;
        if (!string.IsNullOrWhiteSpace(ScopesUrl))
            settings["scopesUrl"] = ScopesUrl;
        if (!string.IsNullOrWhiteSpace(Scopes))
            settings["scopes"] = Scopes;
        if (!string.IsNullOrWhiteSpace(ValenceAppId))
            settings["valenceAppId"] = ValenceAppId;
        if (!string.IsNullOrWhiteSpace(ClientId))
            settings["clientId"] = ClientId;
        return settings;
    }

    public void EnsureStoreCanHoldGrants(SecretStoreDescriptor store) =>
        Elsa.Integrations.Sdk.Secrets.GrantStorePolicy.EnsureWritable(store);
}
