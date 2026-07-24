namespace Elsa.Foundation.Identity.OpenIddict;

public sealed class OpenIddictIdentityOptions
{
    public string ProviderId { get; set; } = "openiddict";

    public string DisplayName { get; set; } = "Elsa Identity";

    public string? TenantId { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsDefault { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// When set, ephemeral per-process signing and encryption keys may be used so a fresh checkout can issue
    /// and validate tokens without key configuration. The selected persistence adapter decides its own
    /// development storage behavior. Issued tokens do not survive a restart.
    /// </summary>
    public bool IsDevelopmentOrDemo { get; set; }

    /// <summary>
    /// The issuer written into (and required from) first-party access tokens. Purely a logical identity —
    /// tokens are validated in-process against the local server options, so this never needs to resolve.
    /// The bearer scheme selector also uses it to tell first-party JWTs from external ones.
    /// </summary>
    public string Issuer { get; set; } = "https://elsa-identity.local/";

    /// <summary>
    /// Key material for signing access tokens (any string; a 256-bit key is derived from it). Falls back to
    /// <c>FoundationIdentityOptions.SigningKey</c>. Required outside <see cref="IsDevelopmentOrDemo"/>.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Key material for OpenIddict's encryption credentials. Defaults to a key derived (with domain
    /// separation) from <see cref="SigningKey"/>, so configuring a single signing key is sufficient.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// The authentication scheme the bearer selector forwards to when an <c>Authorization: Bearer</c> token
    /// was NOT issued by this module (an external IdP JWT). Defaults to the OIDC module's JwtBearer scheme
    /// (<c>OidcAuthenticationOptions.JwtBearerScheme</c> default); only used when that scheme is registered.
    /// </summary>
    public string ExternalBearerScheme { get; set; } = "Elsa.Identity.Oidc.Jwt";

    /// <summary>
    /// The cookie authentication scheme the selector forwards to for interactive (cookie-carrying) requests.
    /// Defaults to the ASP.NET Core Identity module's cookie scheme (<c>AspNetCoreIdentityDefaults.CookieScheme</c>);
    /// only used when that scheme is registered.
    /// </summary>
    public string InteractiveScheme { get; set; } = "Elsa.Identity.Cookie";

    /// <summary>
    /// The request cookie whose presence routes authentication to <see cref="InteractiveScheme"/>.
    /// Matches the cookie name configured by the ASP.NET Core Identity module.
    /// </summary>
    public string InteractiveCookieName { get; set; } = "Elsa.Identity.Cookie";
}
