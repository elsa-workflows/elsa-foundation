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
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityOidc();
    }
}
