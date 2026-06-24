using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityApi",
    DisplayName = "Foundation Identity API",
    Description = "Exposes provider-agnostic identity bootstrap, capability, session, challenge, logout, and token refresh endpoints."
)]
public sealed class FoundationIdentityApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddFoundationIdentityApi();
    }
}
