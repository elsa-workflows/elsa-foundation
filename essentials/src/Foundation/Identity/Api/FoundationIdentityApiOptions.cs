namespace Elsa.Foundation.Identity.Api;

/// <summary>
/// Options for the provider-agnostic identity API surface. Kept in the <c>Api</c> project (which references
/// only the identity abstractions) so endpoints can name the interactive authentication schemes without a
/// project reference to the concrete cookie / external-OIDC modules that define them.
/// </summary>
public sealed class FoundationIdentityApiOptions
{
    /// <summary>
    /// The interactive (session-carrying) authentication schemes the <c>GET /_elsa/identity/token</c>
    /// endpoint authenticates under: the first-party Identity cookie and the external-OIDC JwtBearer scheme.
    /// The cookie→bearer exchange must run under these, never under the first-party bearer validation scheme
    /// (that would be circular). Naming them explicitly also keeps the endpoint working when the composite
    /// scheme selector is not the host's default scheme. Defaults match the well-known scheme names from the
    /// ASP.NET Core Identity and OIDC modules; only unregistered names are dropped at request time.
    /// </summary>
    public IReadOnlyList<string> InteractiveAuthSchemes { get; set; } =
    [
        "Elsa.Identity.Cookie",
        "Elsa.Identity.Oidc.Jwt"
    ];
}
