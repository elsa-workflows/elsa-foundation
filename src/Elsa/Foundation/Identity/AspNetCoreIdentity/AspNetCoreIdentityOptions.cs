namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

/// <summary>
/// Options for the ASP.NET Core Identity-backed authentication provider module.
/// </summary>
public sealed class AspNetCoreIdentityOptions
{
    /// <summary>
    /// The provider id surfaced through <c>bootstrap</c>/<c>capabilities</c>. Kept stable ("aspnetcore-identity")
    /// because downstream clients (and the challenge redirect) key off it.
    /// </summary>
    public string ProviderId { get; set; } = AspNetCoreIdentityDefaults.ProviderId;

    /// <summary>
    /// The human-readable name shown in provider pickers.
    /// </summary>
    public string DisplayName { get; set; } = "Elsa Identity";

    /// <summary>
    /// The tenant this provider serves, or <c>null</c> for the global provider.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Whether the provider is exposed.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether this provider is the default first-party provider.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The tenant assigned to users signing in through the first-party login endpoint when the request does
    /// not specify one.
    /// </summary>
    public string DefaultTenantId { get; set; } = AspNetCoreIdentityDefaults.DefaultTenantId;
}
