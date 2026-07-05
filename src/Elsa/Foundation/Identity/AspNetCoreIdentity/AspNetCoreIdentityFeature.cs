using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentity",
    DisplayName = "Foundation Identity ASP.NET Core Identity",
    Description = "Registers the ASP.NET Core Identity-backed IAM substrate, cookie sign-in surface, and principal factory."
)]
public sealed class AspNetCoreIdentityFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddFoundationAspNetCoreIdentity();
    }
}
