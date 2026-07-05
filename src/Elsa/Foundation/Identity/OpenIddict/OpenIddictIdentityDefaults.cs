namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Stable constants for the OpenIddict identity module: the composite scheme-selector authentication scheme
/// that routes requests to the right handler (first-party bearer, external bearer, or cookie), and the
/// persistence defaults for the companion token-store database.
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
    /// Default Sqlite connection string for the OpenIddict token store. Intentionally the same database file
    /// as the ASP.NET Core Identity module so the out-of-the-box setup keeps all identity state in one place;
    /// the store uses its own migrations-history table so both contexts can migrate independently.
    /// </summary>
    public const string DefaultConnectionString = "Data Source=identity.db";

    /// <summary>Migrations-history table for the companion context, distinct from the identity context's.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_OpenIddict";

    /// <summary>
    /// The custom grant type naming the first-party cookie→bearer exchange driven through
    /// <c>ITokenService</c>. No OAuth endpoint is mounted for it; it satisfies OpenIddict's requirement that
    /// at least one flow is enabled and documents how tokens enter the system.
    /// </summary>
    public const string FirstPartyGrantType = "urn:elsa:identity:first-party";
}
