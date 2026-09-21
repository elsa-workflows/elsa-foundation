namespace Elsa.Foundation.Identity.Oidc;

public sealed class OidcAuthenticationOptions
{
    public string ProviderId { get; set; } = "oidc";

    public string DisplayName { get; set; } = "External OIDC";

    public string AuthenticationScheme { get; set; } = "Elsa.Identity.Oidc";

    public string JwtBearerScheme { get; set; } = "Elsa.Identity.Oidc.Jwt";

    public string? TenantId { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsDefault { get; set; } = true;

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public string ChallengePath { get; set; } = "/_elsa/identity/challenge/oidc";
}
