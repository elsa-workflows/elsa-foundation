namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Stable constants for the provider-neutral OpenIddict identity behavior.
/// </summary>
public static class OpenIddictIdentityDefaults
{
    /// <summary>
    /// The policy ("selector") authentication scheme registered by this module. It never authenticates by
    /// itself: per request it forwards to the OpenIddict validation handler (local bearer tokens), the
    /// configured external JwtBearer scheme (external IdP tokens), or the identity cookie scheme
    /// (interactive requests). Suitable as the host's default authenticate/challenge scheme.
    /// </summary>
    public const string SelectorScheme = "Elsa.Identity.Selector";

    /// <summary>
    /// The custom grant type naming the first-party cookie→bearer exchange driven through
    /// <c>ITokenService</c>. No OAuth endpoint is mounted for it; it satisfies OpenIddict's requirement that
    /// at least one flow is enabled and documents how tokens enter the system.
    /// </summary>
    public const string FirstPartyGrantType = "urn:elsa:identity:first-party";
}
