using CShells.Features;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.OpenIddict;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityOpenIddict",
    DisplayName = "Foundation Identity OpenIddict",
    Description = "Registers the OpenIddict reference authentication provider and first-party token service."
)]
public sealed class OpenIddictIdentityFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityOpenIddict();
    }
}
