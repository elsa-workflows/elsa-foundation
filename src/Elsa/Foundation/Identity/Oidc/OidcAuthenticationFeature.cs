using CShells.Features;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
public sealed class OidcAuthenticationFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Is default provider", Description = "Advertises the external OIDC provider as the default in bootstrap. Turn off when first-party (ASP.NET Core Identity) login should be the default sign-in experience.", Category = "Identity", DefaultValue = "true")]
    public bool IsDefault { get; set; } = true;

    /* LOCAL WORKAROUND - NOT a committed change; revert with `git checkout <this file>`.
     *
     * THE FEATURE EXPOSED ONLY IsDefault, SO THE PROVIDER COULD NOT BE POINTED AT AN ISSUER AT ALL.
     * ConfigureOidcJwtBearerOptions copies OidcAuthenticationOptions.Authority/ClientId onto the JwtBearer
     * handler, but NOTHING binds OidcAuthenticationOptions from configuration - not this feature, not a
     * configuration section, and no host in this tree sets it. So Authority stayed null, JwtBearer had no
     * issuer to validate against, and every bearer token was refused with
     *     WWW-Authenticate: Bearer error="invalid_token",
     *      error_description="The issuer '<the correct issuer>' is invalid"
     * which reads as a misconfigured ISSUER rather than as an unconfigurable CLIENT.
     *
     * Measured downstream 2026-09-06: nexxbiz-elsa-suite's shipped analytics preset already sets
     * "FoundationIdentityOidc": { "Authority": ..., "RequireHttpsMetadata": false } in shells.json and
     * carries a note explaining what happens "with no ClientId" - written against a surface the feature
     * does not have, so those keys were silently ignored. */
    [ManifestSetting(DisplayName = "Authority", Description = "The OIDC authority (issuer) this shell trusts. Required for bearer validation - without it no token can be accepted.", Category = "Identity")]
    public string? Authority { get; set; }

    [ManifestSetting(DisplayName = "Client id", Description = "This shell's client id. Also used as the expected bearer AUDIENCE, so a token minted for a different audience is refused.", Category = "Identity")]
    public string? ClientId { get; set; }

    [ManifestSetting(DisplayName = "Client secret", Description = "Client secret for the interactive code flow. Not needed for bearer validation.", Category = "Identity", Secret = true)]
    public string? ClientSecret { get; set; }

    [ManifestSetting(DisplayName = "Require HTTPS metadata", Description = "Leave true outside development. HTTP metadata is only safe on a private network the deployment controls.", Category = "Identity", DefaultValue = "true")]
    public bool RequireHttpsMetadata { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityOidc(options =>
        {
            options.IsDefault = IsDefault;

            // Assigned only when set, so composing the feature with no authority keeps whatever a host
            // configured elsewhere rather than blanking it.
            if (!string.IsNullOrWhiteSpace(Authority)) options.Authority = Authority;
            if (!string.IsNullOrWhiteSpace(ClientId)) options.ClientId = ClientId;
            if (!string.IsNullOrWhiteSpace(ClientSecret)) options.ClientSecret = ClientSecret;

            options.RequireHttpsMetadata = RequireHttpsMetadata;
        });
    }
}
