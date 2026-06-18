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
}
