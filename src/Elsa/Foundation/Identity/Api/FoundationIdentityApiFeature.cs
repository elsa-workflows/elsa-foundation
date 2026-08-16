using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityApi",
    DisplayName = "Foundation Identity API",
    Description = "Exposes provider-agnostic identity bootstrap, capability, session, challenge, logout, and token refresh endpoints."
)]
public class FoundationIdentityApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityApi();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        FoundationIdentityApi.MapFoundationIdentityApi(endpoints);
}
