using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentityGroundwork",
    DisplayName = "Foundation Identity ASP.NET Core Identity Groundwork",
    Description = "Registers the Groundwork-backed ASP.NET Core Identity stores and Elsa IAM adapters.",
    Metadata = [GroundworkSchemaFeatureMetadata.Key, GroundworkSchemaFeatureMetadata.Identity])]
public class AspNetCoreIdentityGroundworkFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddFoundationAspNetCoreIdentityGroundwork();
}
