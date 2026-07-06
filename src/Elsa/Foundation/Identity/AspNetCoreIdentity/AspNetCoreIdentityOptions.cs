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

    /// <summary>
    /// Absolute origins (scheme + host + port, e.g. <c>https://localhost:7030</c>) that a post-login
    /// <c>returnUrl</c> may redirect to in addition to same-origin local paths. Needed when Studio is hosted
    /// on a different origin than the backend: without its origin listed here the open-redirect guard strips
    /// the cross-origin return URL and the user lands on the backend instead of back in Studio. Keep this in
    /// sync with the trusted Studio origins (the same ones allowed for credentialed CORS).
    /// </summary>
    public IList<string> AllowedReturnUrlOrigins { get; set; } = new List<string>();
}
