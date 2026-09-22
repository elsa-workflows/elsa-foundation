using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("workflows", "Workflows", "1.0.8", "navigation", "routes", "http", "workflow-designer")]
[ShellFeature(
    name: "WorkflowsStudio",
    DisplayName = "Workflows Studio module",
    Description = "Contributes workflow definition management and design routes to Elsa Studio."
)]
public sealed class WorkflowsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
