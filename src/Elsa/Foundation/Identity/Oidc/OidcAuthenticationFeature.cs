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

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityOidc(options => options.IsDefault = IsDefault);
    }
}
