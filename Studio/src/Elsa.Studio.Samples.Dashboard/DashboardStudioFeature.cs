using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Samples.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Dashboard")]
[ManifestFeatureCategory("Samples")]
[StudioModule("dashboard", "Dashboard sample", "1.0.0", "navigation", "routes", "dashboard-widgets")]
[ShellFeature(
    name: "DashboardSample",
    DisplayName = "Dashboard sample module",
    Description = "Contributes the frontend-only dashboard sample Studio module manifest."
)]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
