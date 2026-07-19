using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Dashboard")]
[StudioModule("dashboard", "Dashboard", "1.0.0", "navigation", "routes", "dashboard-widgets")]
[ShellFeature(name: "DashboardStudio", DisplayName = "Dashboard", Description = "Hosts the customizable Studio dashboard.")]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
