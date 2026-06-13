using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Admin.Web;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Admin")]
[ManifestFeatureCategory("Web")]
[ShellFeature(
    name: "AdminWeb",
    Description = "Provides the React admin shell static web assets."
)]
public class AdminWebFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
