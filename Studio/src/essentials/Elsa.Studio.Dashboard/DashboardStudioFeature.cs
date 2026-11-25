using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Dashboard")]
[StudioModule("dashboard", "Dashboards", "navigation", "routes")]
[ShellFeature(name: "DashboardStudio", DisplayName = "Dashboards", Description = "Saved questions and named dashboards. Home belongs to the Studio shell.")]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
