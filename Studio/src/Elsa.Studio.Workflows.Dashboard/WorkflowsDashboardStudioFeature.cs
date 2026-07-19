using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Workflows.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("workflows-dashboard", "Workflow dashboard widgets", "1.0.0", "dashboard-widgets", "http")]
[ShellFeature(name: "WorkflowsDashboardStudio", DisplayName = "Workflow dashboard widgets", Description = "Contributes workflow dashboard widgets.")]
public sealed class WorkflowsDashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
