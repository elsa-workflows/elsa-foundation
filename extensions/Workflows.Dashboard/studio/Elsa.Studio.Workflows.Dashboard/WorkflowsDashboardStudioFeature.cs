using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Workflows.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("workflows-dashboard", "Workflow dashboard widgets", "dashboard-widgets", "http")]
[ShellFeature(name: "WorkflowsDashboardStudio", DisplayName = "Workflow dashboard widgets", Description = "Contributes the workflow task group and workflow widgets to Studio Home.")]
public sealed class WorkflowsDashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
