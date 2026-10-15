using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Dashboard")]
[StudioModule("dashboard", "Home", "navigation", "routes", "dashboard-widgets")]
[ShellFeature(name: "DashboardStudio", DisplayName = "Home", Description = "Hosts the customizable Studio Home.")]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
