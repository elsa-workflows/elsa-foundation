using CShells.Features;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Oidc;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityOidc",
    DisplayName = "Foundation Identity OIDC",
    Description = "Registers the external OIDC authentication provider module and ASP.NET Core OIDC/JWT handlers."
)]
public class OidcAuthenticationFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Is default provider", Description = "Advertises the external OIDC provider as the default in bootstrap. Turn off when first-party (ASP.NET Core Identity) login should be the default sign-in experience.", Category = "Identity", DefaultValue = "true")]
    public bool IsDefault { get; set; } = true;

    [ManifestSetting(DisplayName = "Authority", Description = "The external IdP's issuer / discovery authority, e.g. https://keycloak.example.com/realms/elsa. Bearer tokens are validated against its discovery document; without it every external token is refused.", Category = "Identity")]
    public string? Authority { get; set; }

    [ManifestSetting(DisplayName = "Client ID", Description = "The OAuth client id registered with the IdP. Enables the interactive sign-in (OpenID Connect) handler, supplies the legacy bearer audience only when Audience is absent.", Category = "Identity")]
    public string? ClientId { get; set; }

    [ManifestSetting(DisplayName = "Client secret", Description = "Client secret for the confidential authorization-code flow. Supply via a secret (user-secrets/environment variable), never in committed config.", Category = "Security", Secret = true)]
    public string? ClientSecret { get; set; }

    [ManifestSetting(DisplayName = "Require HTTPS metadata", Description = "Requires the IdP's discovery metadata to be served over HTTPS. Keep on in production; turn off only for a local IdP served over plain HTTP.", Category = "Security", DefaultValue = "true")]
    public bool RequireHttpsMetadata { get; set; } = true;

    [ManifestSetting(DisplayName = "Normalize bearer claims", Description = "Normalize validated API tokens using current mapping rules in the fixed provider and tenant. Requires a matching host persistence scope.", Category = "Identity", DefaultValue = "false")]
    public bool NormalizeBearerClaims { get; set; }

    [ManifestSetting(DisplayName = "API audience", Description = "Bearer token audience, independent of interactive sign-in. Absent uses Client ID; explicit blank refuses normalization activation.", Category = "Identity")]
    public string? Audience { get; set; }

    [ManifestSetting(DisplayName = "Provider ID", Description = "Static provider namespace for mapping rules.", Category = "Identity", DefaultValue = "oidc")]
    public string ProviderId { get; set; } = "oidc";

    [ManifestSetting(DisplayName = "Tenant ID", Description = "Static tenant namespace. Normalization requires the host to initialize a matching ordinary persistence scope.", Category = "Identity")]
    public string? TenantId { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Capture setting values locally: the feature instance is configuration-bound before this runs.
        var isDefault = IsDefault;
        var authority = Authority;
        var clientId = ClientId;
        var clientSecret = ClientSecret;
        var requireHttpsMetadata = RequireHttpsMetadata;
        var normalizeBearerClaims = NormalizeBearerClaims;
        var audience = Audience;
        var providerId = ProviderId;
        var tenantId = TenantId;

        // Applied through the configure delegate rather than services.Configure: AddFoundationIdentityOidc
        // evaluates it at registration time to decide whether to register the interactive OpenID Connect
        // handler, which it does only when a ClientId is present.
        services.AddFoundationIdentityOidc(options =>
        {
            options.IsDefault = isDefault;
            options.NormalizeBearerClaims = normalizeBearerClaims;
            options.Audience = audience;
            options.ProviderId = providerId;
            options.TenantId = tenantId;
            options.RequireHttpsMetadata = requireHttpsMetadata;

            if (!string.IsNullOrWhiteSpace(authority))
                options.Authority = authority;

            if (!string.IsNullOrWhiteSpace(clientId))
                options.ClientId = clientId;

            if (!string.IsNullOrWhiteSpace(clientSecret))
                options.ClientSecret = clientSecret;
        });
    }
}
