using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Dashboard")]
[ShellFeature(name: "DashboardStudio", DisplayName = "Dashboard", Description = "Hosts the customizable Studio dashboard.")]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddDashboardStudio();
}
