using CShells.Features;
using Elsa.Events.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Admin.Samples.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Admin")]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: "AdminSamplesDashboard",
    Description = "Sample frontend-only admin module that contributes dashboard UI."
)]
public class DashboardAdminSampleFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddEventHandlersFrom(GetType().Assembly);
    }
}
