using CShells.Features;
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
    Description = "Registers the ASP.NET Core Identity-backed IAM substrate and principal factory."
)]
public sealed class AspNetCoreIdentityFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAspNetCoreIdentity();
    }
}
