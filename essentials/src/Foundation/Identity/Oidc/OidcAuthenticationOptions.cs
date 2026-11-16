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

    /// <summary>Enables guarded per-request claim normalization. Requires a fixed provider, tenant and host persistence scope.</summary>
    public bool NormalizeBearerClaims { get; set; }

    /// <summary>Bearer audience. Only absence falls back to ClientId; an explicit blank remains blank.</summary>
    public string? Audience { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public string ChallengePath { get; set; } = "/_elsa/identity/challenge/oidc";
}
